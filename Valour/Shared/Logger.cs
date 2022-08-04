namespace Valour.Shared;

public static class Logger
{
    public static event Func<string, string, Task> OnLog;

    public static async Task Log(string text, string color = "white")
    {
        if (OnLog != null)
            await OnLog?.Invoke(text, color);
    }
}
