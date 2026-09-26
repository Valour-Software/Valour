namespace Valour.Sdk.Services;

public class LoggingService
{
    // Loggers are added and removed by UI components while log calls arrive
    // from any thread, so each call works on a snapshot taken under the lock.
    private readonly Lock _lock = new();
    private readonly List<Action<string, string>> _loggers = new();
    private readonly List<Action<string, string, string>> _colorLoggers = new();

    public LoggingService(bool addDefaultLogger = true)
    {
        if (addDefaultLogger)
            _loggers.Add(DefaultLog);
    }

    private void DefaultLog(string prefix, string message)
    {
        Console.WriteLine($"[{prefix}] {message}");
    }

    public void AddLogger(Action<string, string> logger)
    {
        lock (_lock)
            _loggers.Add(logger);
    }

    public void AddColorLogger(Action<string, string, string> logger)
    {
        lock (_lock)
            _colorLoggers.Add(logger);
    }

    public void RemoveLogger(Action<string, string> logger)
    {
        lock (_lock)
            _loggers.Remove(logger);
    }

    public void RemoveColorLogger(Action<string, string, string> logger)
    {
        lock (_lock)
            _colorLoggers.Remove(logger);
    }

    public void Log(string prefix, string message, string color)
    {
        Action<string, string>[] loggers;
        Action<string, string, string>[] colorLoggers;
        lock (_lock)
        {
            loggers = _loggers.ToArray();
            colorLoggers = _colorLoggers.ToArray();
        }

        foreach (var logger in loggers)
        {
            logger(prefix, message);
        }

        foreach (var logger in colorLoggers)
        {
            logger(prefix, message, color);
        }
    }

    public void Log<T>(string message, string color) => Log(typeof(T).Name, message, color);
}
