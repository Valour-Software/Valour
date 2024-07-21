namespace Valour.Client.Toast;

public enum ToastProgressState
{
    Running,
    Success,
    Failure
}

public class ToastData
{
    public ToastCard Card { get; set; }
    
    public string Title { get; set; }
    public string Message { get; set; }
    public bool ShouldAutoClose { get; set; } = true;

    public virtual bool AutoClose => ShouldAutoClose;
    
    public ToastData()
    {
        
    }
    
    public ToastData(string title, string message)
    {
        Title = title;
        Message = message;
    }
}

public abstract class ProgressToastDataBase : ToastData
{
    public string SuccessMessage { get; set; }
    public string FailureMessage { get; set; }
    
    public override bool AutoClose => false;

    public ProgressToastDataBase()
    {
        
    }
    
    public ProgressToastDataBase(string title, string message) : base(title, message)
    {
        
    }
}

public class ProgressToastData : ProgressToastDataBase
{
    public Task ProgressTask { get; set; }

    public ProgressToastData()
    {
        
    }
    
    public ProgressToastData(string title, string message, Task progressTask, string successMessage = null, string failureMessage = null) : base(title, message)
    {
        Title = title;
        Message = message;
        ProgressTask = progressTask;
        SuccessMessage = successMessage;
        FailureMessage = failureMessage;
    }
}

public class ProgressToastData<T> : ProgressToastDataBase
{
    public Task<T> ProgressTask { get; set; }
    
    public ProgressToastData()
    {
        
    }
    
    public ProgressToastData(string title, string message, Task<T> progressTask, string successMessage = null, string failureMessage = null) : base(title, message)
    {
        Title = title;
        Message = message;
        ProgressTask = progressTask;
        SuccessMessage = successMessage;
        FailureMessage = failureMessage;
    }
}

