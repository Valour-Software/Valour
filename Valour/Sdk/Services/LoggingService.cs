namespace Valour.SDK.Services;

public class LoggingService
{
    private List<Action<string, string>> Loggers { get; set; } = new();
    private List<Action<string, string, string>> ColorLoggers { get; set; } = new();

    public LoggingService(bool addDefaultLogger = true)
    {
        if (addDefaultLogger)
            Loggers.Add(DefaultLog);
    }

    private void DefaultLog(string prefix, string message)
    {
        Console.WriteLine($"[{prefix}] {message}");
    }
    
    public void AddLogger(Action<string, string> logger)
    {
        Loggers.Add(logger);
    }
    
    public void AddColorLogger(Action<string, string, string> logger)
    {
        ColorLoggers.Add(logger);
    }
    
    public void Log(string prefix, string message, string color)
    {
        foreach (var logger in Loggers)
        {
            logger(prefix, message);
        }
        
        foreach (var logger in ColorLoggers)
        {
            logger(prefix, message, color);
        }
    }
}