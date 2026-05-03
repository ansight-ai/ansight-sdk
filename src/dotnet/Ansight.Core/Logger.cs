namespace Ansight;

/// <summary>
/// Lightweight logging hub used by Ansight; consumers can register callbacks to receive diagnostics.
/// </summary>
public static class Logger
{
    private static readonly Lock LoggerLock = new Lock();

    private static readonly List<ILogCallback> Callbacks = new List<ILogCallback>();


    /// <summary>
    /// Registers a <see cref="ILogCallback"/> into the logger, which will receive all log messages recorded by Ansight.
    /// </summary>
    public static void RegisterCallback(ILogCallback callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));

        if (string.IsNullOrWhiteSpace(callback.Name))
        {
            throw new ArgumentException($"The provided logging callback must have a name.", nameof(callback));
        }
        
        lock (LoggerLock)
        {
            if (Callbacks.Contains(callback))
            {
                throw new InvalidOperationException($"The {nameof(ILogCallback)} instance, '{callback.GetHashCode()}', is already registered.");
            }

            if (Callbacks.Any(a => string.Equals(a.Name, callback.Name, StringComparison.InvariantCultureIgnoreCase)))
            {
                throw new InvalidOperationException($"The {nameof(ILogCallback)} instance must have a unique name. A callback with the name {callback.Name} already exists.");
            }
            
            Callbacks.Add(callback);
        }
    }

    public static void RemoveCallback(ILogCallback callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));

        lock (LoggerLock)
        {
            Callbacks.Remove(callback);
        }
    }

    private static void Broadcast(Action<ILogCallback> invoke)
    {
        lock (LoggerLock)
        {
            for (int i = Callbacks.Count - 1; i >= 0; i--)
            {
                var callback = Callbacks[i];
                if (callback is null)
                {
                    Callbacks.RemoveAt(i); 
                    continue;
                }

                try
                {
                    invoke(callback);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{Constants.LoggingPrefix} 🚨 The logging callback {callback.Name}|{callback.GetHashCode()} failed {e}. This callback will be automatically de-registered");

                    try
                    {
                        Callbacks.RemoveAt(i);
                    }
                    catch (Exception removeEx)
                    {
                        Console.WriteLine($"{Constants.LoggingPrefix} ⚠️ Failed to deregister callback {callback.Name}|{callback.GetHashCode()}: {removeEx}");
                    }
                }
            }
        }
    }

    public static void Error(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;    
        }
        
        Broadcast(cb => cb.Error(message));
    }

    public static void Warning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;    
        }
        
        Broadcast(cb => cb.Warning(message));
    }

    public static void Info(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;    
        }
        
        Broadcast(cb => cb.Info(message));
    }

    public static void Exception(Exception exception)
    {
        if (exception == null)
        {
            return;
        }
        
        Broadcast(cb => (cb).Exception(exception));
    }

}
