namespace Ansight;

/// <summary>
/// Simple console-based logger for Ansight diagnostics.
/// </summary>
public class ConsoleLogger : ILogCallback
{
    public string Name { get; } = "Ansight Console Logger";
    
    public void Error(string message)
    {
        Console.WriteLine($"{Constants.LoggingPrefix} (Error)" + message);
    }

    public void Warning(string message)
    {
        Console.WriteLine($"{Constants.LoggingPrefix} (Warning)" + message);
    }

    public void Info(string message)
    {
        Console.WriteLine($"{Constants.LoggingPrefix} " + message);
    }

    public void Exception(Exception exception)
    {
        Console.WriteLine($"{Constants.LoggingPrefix} (Exception)" + exception);
    }
}
