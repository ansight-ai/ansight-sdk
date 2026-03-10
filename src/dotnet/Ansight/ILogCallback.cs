namespace Ansight;

/// <summary>
/// Abstraction for consuming Ansight log output.
/// </summary>
public interface ILogCallback
{
    /// <summary>
    /// Human-readable sink name for diagnostics.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Logs an error message.
    /// </summary>
    void Error(string message);
    
    /// <summary>
    /// Logs a warning message.
    /// </summary>
    void Warning(string message);
    
    /// <summary>
    /// Logs an informational message.
    /// </summary>
    void Info(string message);
    
    /// <summary>
    /// Logs an exception.
    /// </summary>
    void Exception(Exception exception);
}
