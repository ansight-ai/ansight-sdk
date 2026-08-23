namespace Ansight.Screenshot;

internal sealed class LatestCaptureBuffer<T> : IDisposable
    where T : class, IDisposable
{
    private readonly Lock stateLock = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private T? pendingValue;
    private bool completed;
    private bool disposed;

    public bool Submit(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        T? replacedValue;
        var accepted = true;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed)
            {
                replacedValue = value;
                accepted = false;
            }
            else
            {
                replacedValue = pendingValue;
                pendingValue = value;
                if (signal.CurrentCount == 0)
                {
                    signal.Release();
                }
            }
        }

        replacedValue?.Dispose();
        return accepted && replacedValue is not null;
    }

    public async Task<T?> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await signal.WaitAsync(cancellationToken);

            lock (stateLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (pendingValue is not null)
                {
                    var value = pendingValue;
                    pendingValue = null;
                    if (completed && signal.CurrentCount == 0)
                    {
                        signal.Release();
                    }
                    return value;
                }

                if (completed)
                {
                    return null;
                }
            }
        }
    }

    public void Complete()
    {
        lock (stateLock)
        {
            if (completed || disposed)
            {
                return;
            }

            completed = true;
            if (signal.CurrentCount == 0)
            {
                signal.Release();
            }
        }
    }

    public void Dispose()
    {
        T? pendingValue;
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            completed = true;
            pendingValue = this.pendingValue;
            this.pendingValue = null;
        }

        pendingValue?.Dispose();
        signal.Dispose();
    }
}
