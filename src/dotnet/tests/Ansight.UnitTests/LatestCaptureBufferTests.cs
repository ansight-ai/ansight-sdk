using Ansight.Screenshot;

namespace Ansight.UnitTests;

public sealed class LatestCaptureBufferTests
{
    [Fact]
    public async Task SubmitReplacesAndDisposesAStalePendingValue()
    {
        using var buffer = new LatestCaptureBuffer<TestCapture>();
        var first = new TestCapture();
        using var second = new TestCapture();

        Assert.False(buffer.Submit(first));
        Assert.True(buffer.Submit(second));
        Assert.True(first.IsDisposed);

        var read = await buffer.ReadAsync(CancellationToken.None);
        Assert.Same(second, read);

        buffer.Complete();
        Assert.Null(await buffer.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SubmitAfterCompletionDisposesTheRejectedValue()
    {
        using var buffer = new LatestCaptureBuffer<TestCapture>();
        var rejected = new TestCapture();
        buffer.Complete();

        Assert.False(buffer.Submit(rejected));
        Assert.True(rejected.IsDisposed);
        Assert.Null(await buffer.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CompletionDrainsTheLatestPendingValueBeforeEnding()
    {
        using var buffer = new LatestCaptureBuffer<TestCapture>();
        using var pending = new TestCapture();
        Assert.False(buffer.Submit(pending));

        buffer.Complete();

        Assert.Same(pending, await buffer.ReadAsync(CancellationToken.None));
        Assert.Null(await buffer.ReadAsync(CancellationToken.None));
    }

    private sealed class TestCapture : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
