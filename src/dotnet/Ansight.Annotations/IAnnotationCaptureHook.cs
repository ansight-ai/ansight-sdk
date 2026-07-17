namespace Ansight.Annotations;

/// <summary>
/// Adds host-app custom data or artifacts while an annotation is being captured.
/// </summary>
public interface IAnnotationCaptureHook
{
    ValueTask ContributeAsync(AnnotationCaptureContext context, CancellationToken cancellationToken);
}
