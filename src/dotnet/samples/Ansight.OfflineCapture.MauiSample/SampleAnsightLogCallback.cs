namespace Ansight.OfflineCapture.MauiSample;

public sealed class SampleAnsightLogCallback : ILogCallback
{
    public string Name => "Offline Capture MAUI Sample";

    public void Error(string message)
    {
        Console.WriteLine("Ansight error: " + message);
    }

    public void Warning(string message)
    {
        Console.WriteLine("Ansight warning: " + message);
    }

    public void Info(string message)
    {
        Console.WriteLine("Ansight info: " + message);
    }

    public void Exception(Exception exception)
    {
        Console.WriteLine("Ansight exception: " + exception);
    }
}
