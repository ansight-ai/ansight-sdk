namespace Ansight.Annotations;

internal sealed class AnnotationService
{
    private readonly IRuntime runtime;
    private readonly AnnotationOptions options;
    private readonly AnnotationEvidenceCapture evidenceCapture;
    private readonly AnnotationOutbox outbox;
    private readonly TimeProvider timeProvider;
    private readonly AnnotationOverlayPresentation overlayPresentation;
    private readonly SemaphoreSlim presentationGate = new(1, 1);
    private readonly SemaphoreSlim deliveryGate = new(1, 1);

    internal AnnotationService(IRuntime runtime, AnnotationOptions options)
        : this(runtime, options, TimeProvider.System, AnnotationOverlayPresenter.PresentAsync)
    {
    }

    internal AnnotationService(
        IRuntime runtime,
        AnnotationOptions options,
        TimeProvider timeProvider,
        AnnotationOverlayPresentation overlayPresentation)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.options = options?.Normalize() ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.overlayPresentation = overlayPresentation ?? throw new ArgumentNullException(nameof(overlayPresentation));
        evidenceCapture = new AnnotationEvidenceCapture(this.options);
        outbox = new AnnotationOutbox(this.options.OutboxDirectory);
        runtime.HostConnection.StatusChanged += HandleHostConnectionStatusChanged;
        if (runtime.HostConnection.IsConnected)
        {
            _ = FlushOutboxSafelyAsync();
        }
    }

    internal async Task<AnnotationCaptureResult> CaptureAsync(
        AnnotationCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var origin = BeginCapture();
        try
        {
            var evidence = await evidenceCapture.CaptureAsync(origin.CaptureGroupId, cancellationToken);
            return await CompleteAsync(origin, request, evidence, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new AnnotationCaptureResult(AnnotationCaptureStatus.Failed, message: exception.Message);
        }
    }

    internal async Task<AnnotationCaptureResult> PresentAsync(
        object? overlayHost,
        CancellationToken cancellationToken)
    {
        var origin = BeginCapture();
        await presentationGate.WaitAsync(cancellationToken);
        try
        {
            var evidence = await evidenceCapture.CaptureAsync(origin.CaptureGroupId, cancellationToken);
            var overlay = await overlayPresentation(evidence.Screenshot, overlayHost, cancellationToken);
            if (overlay.IsCancelled)
            {
                return new AnnotationCaptureResult(
                    AnnotationCaptureStatus.Cancelled,
                    message: "Feedback capture was cancelled.",
                    evidence: evidence.Results);
            }

            if (!overlay.IsSubmitted || overlay.Request is null)
            {
                return new AnnotationCaptureResult(
                    AnnotationCaptureStatus.Unavailable,
                    message: overlay.Message ?? "The feedback overlay is unavailable.",
                    evidence: evidence.Results);
            }

            return await CompleteAsync(origin, overlay.Request, evidence, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new AnnotationCaptureResult(AnnotationCaptureStatus.Failed, message: exception.Message);
        }
        finally
        {
            presentationGate.Release();
        }
    }

    private async Task<AnnotationCaptureResult> CompleteAsync(
        AnnotationCaptureOrigin origin,
        AnnotationCaptureRequest request,
        AnnotationEvidenceSnapshot evidence,
        CancellationToken cancellationToken)
    {
        var context = new AnnotationCaptureContext(
            origin.AnnotationId,
            origin.RequestedAtUtc,
            request,
            evidence.Results);
        foreach (var hook in options.Hooks)
        {
            try
            {
                await hook.ContributeAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                context.RecordHookFailure(hook, exception);
            }
        }

        var bundle = await AnnotationBundleWriter.CreateAsync(
            origin.AnnotationId,
            origin.RequestedAtUtc,
            request,
            evidence,
            context,
            options.MaximumArtifactBytes,
            cancellationToken);

        string? outboxPath = null;
        string? outboxError = null;
        try
        {
            outboxPath = await outbox.StoreAsync(bundle, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            outboxError = exception.Message;
        }

        IReadOnlyList<AnnotationSinkResult> sinkResults;
        await deliveryGate.WaitAsync(cancellationToken);
        try
        {
            sinkResults = await SubmitToSinksAsync(bundle, GetDeliverySinks(), cancellationToken);
        }
        finally
        {
            deliveryGate.Release();
        }

        var delivered = sinkResults.Any(result => result.IsSuccess);
        if (delivered && outboxPath is not null)
        {
            outbox.Remove(outboxPath);
            outboxPath = null;
        }

        if (delivered)
        {
            return new AnnotationCaptureResult(
                AnnotationCaptureStatus.Completed,
                origin.AnnotationId,
                "Annotation captured.",
                evidence: evidence.Results,
                sinks: sinkResults);
        }

        if (outboxPath is not null)
        {
            return new AnnotationCaptureResult(
                AnnotationCaptureStatus.Queued,
                origin.AnnotationId,
                "Annotation captured and retained in the local outbox.",
                outboxPath,
                evidence.Results,
                sinkResults);
        }

        return new AnnotationCaptureResult(
            AnnotationCaptureStatus.Failed,
            origin.AnnotationId,
            $"Annotation delivery and local persistence failed. {outboxError}",
            evidence: evidence.Results,
            sinks: sinkResults);
    }

    private async Task<IReadOnlyList<AnnotationSinkResult>> SubmitToSinksAsync(
        AnnotationBundle bundle,
        IReadOnlyList<IAnnotationSink> sinks,
        CancellationToken cancellationToken)
    {
        var results = new List<AnnotationSinkResult>();
        foreach (var sink in sinks)
        {
            try
            {
                var sinkResult = await sink.SubmitAsync(bundle, cancellationToken);
                results.Add(sinkResult ?? AnnotationSinkResult.Failure(sink.Id, "The sink returned no result."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(AnnotationSinkResult.Failure(sink.Id, exception.Message));
            }
        }

        return results;
    }

    private void HandleHostConnectionStatusChanged(object? sender, HostConnectionChangedEventArgs args)
    {
        if (args.Status.IsConnected)
        {
            _ = FlushOutboxSafelyAsync();
        }
    }

    private async Task FlushOutboxSafelyAsync()
    {
        try
        {
            await deliveryGate.WaitAsync();
            try
            {
                var pending = await outbox.LoadPendingAsync(CancellationToken.None);
                foreach (var item in pending)
                {
                    var results = await SubmitToSinksAsync(
                        item.Bundle,
                        GetDeliverySinks(),
                        CancellationToken.None);
                    if (results.Any(result => result.IsSuccess))
                    {
                        outbox.Remove(item.Path);
                    }
                }
            }
            finally
            {
                deliveryGate.Release();
            }
        }
        catch (Exception exception)
        {
            Logger.Warning($"Unable to flush the annotation outbox: {exception.Message}");
        }
    }

    private IReadOnlyList<IAnnotationSink> GetDeliverySinks()
    {
        var sinks = new List<IAnnotationSink>();
        if (runtime.HostConnection is not null)
        {
            sinks.Add(new LiveAnnotationSink(runtime));
        }

        foreach (var sink in Feedback.GetSinks(runtime))
        {
            var existingIndex = sinks.FindIndex(existing => string.Equals(existing.Id, sink.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                sinks[existingIndex] = sink;
            }
            else
            {
                sinks.Add(sink);
            }
        }

        return sinks;
    }

    private AnnotationCaptureOrigin BeginCapture()
    {
        var requestedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        return new AnnotationCaptureOrigin(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            requestedAtUtc);
    }

    private sealed record AnnotationCaptureOrigin(
        Guid AnnotationId,
        Guid CaptureGroupId,
        DateTimeOffset RequestedAtUtc);
}
