namespace Ansight.UnitTests;

public sealed class UnhandledExceptionCaptureTests
{
    [Fact]
    public void FatalExceptionIsPersistedSynchronously()
    {
        var exception = new InvalidOperationException("crash evidence");
        object? captured = null;
        var capture = new UnhandledExceptionCapture((value, fatal) =>
        {
            Assert.True(fatal);
            captured = value;
        });
        capture.Record(exception, fatal: true);
        Assert.Same(exception, captured);
    }

    [Fact]
    public void AndroidBoundaryExceptionPreservesEvidenceWithoutDeclaringHandledExceptionsFatal()
    {
        var exception = new InvalidOperationException("UI callback");
        var records = new List<object>();
        bool? capturedFatal = null;
        var capture = new UnhandledExceptionCapture((value, fatal) =>
        {
            records.Add(value);
            capturedFatal = fatal;
        });
        capture.RecordAndroidException(exception, handled: true);
        Assert.Empty(records);
        capture.RecordAndroidException(exception, handled: false);
        Assert.Same(exception, Assert.Single(records));
        Assert.False(capturedFatal);
    }

    [Fact]
    public void PersistenceFailureDoesNotEscapeAndSubsequentCaptureStillWorks()
    {
        var calls = 0;
        var capture = new UnhandledExceptionCapture((_, _) =>
        {
            calls++;
            throw new IOException("disk unavailable");
        });
        capture.Record(new Exception(), fatal: true);
        capture.Record(new Exception(), fatal: true);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void RecursiveCaptureDoesNotReenterPersistence()
    {
        var calls = 0;
        UnhandledExceptionCapture? capture = null;
        capture = new UnhandledExceptionCapture((value, fatal) =>
        {
            calls++;
            capture!.Record(value, fatal);
        });
        capture.Record(new Exception(), fatal: true);
        Assert.Equal(1, calls);
    }
}
