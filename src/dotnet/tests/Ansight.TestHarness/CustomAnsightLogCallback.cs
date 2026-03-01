namespace Ansight.TestHarness;

public class CustomAnsightLogCallback : ILogCallback
{
    public string Name { get; } = "Custom Log Callback";
    
    public void Error(string message)
    {
        Console.WriteLine("🔴 ERROR: "+ message);
    }

    public void Warning(string message)
    {
        Console.WriteLine("🟠 Warning: " + message);
    }

    public void Info(string message)
    {
        Console.WriteLine("🔵 Info: " + message);
    }

    public void Exception(Exception exception)
    {
        Console.WriteLine("🚩 Exception: "+ exception);
    }
}