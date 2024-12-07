using Valour.Shared;

namespace Valour.Sdk.Services;

public class LogOptions
{
    public readonly string Prefix;
    public readonly string Color;
    public readonly string ErrorColor;
    public readonly string WarningColor;
    
    public LogOptions(string prefix = "?", string color = "white", string errorColor = "red", string warningColor = "yellow")
    {
        Prefix = prefix;
        Color = color;
        ErrorColor = errorColor;
        WarningColor = warningColor;
    }
}

public abstract class ServiceBase
{
    private LogOptions _logOptions;
    private LoggingService _logger;
    
    protected void SetupLogging(LoggingService logger, LogOptions options = null)
    {
        _logger = logger;
        _logOptions = options ?? new LogOptions();
    }
    
    protected void Log(string message)
    {
        _logger.Log(_logOptions.Prefix, message, _logOptions.Color);
    }
    
    protected void LogError(string message)
    {
        _logger.Log(_logOptions.Prefix, message, _logOptions.ErrorColor);
    }
    
    protected void LogError(string message, ITaskResult result)
    {
        _logger.Log(_logOptions.Prefix, message + "\n \n" + result.Message, _logOptions.ErrorColor);
    }

    protected void LogIfError(string message, ITaskResult result)
    {
        if (result.Success)
            return;
        
        LogError(message + "\n \n" + result.Message);
    }
    
    protected void LogError(string message, Exception ex)
    {
        LogError(message + "\n \n Exception: \n \n" + ex.Message);
    }
    
    protected void LogWarning(string message)
    {
        _logger.Log(_logOptions.Prefix, message, _logOptions.WarningColor);
    }
}