using Valour.Shared;

namespace Valour.Client.Blazor;

public static class ColorLogger
{
	public static App App { get; set; }

	public static void Setup()
	{
        Logger.OnLog += Log;
    }

	public static async Task Log(string message, string color = null)
	{
		await App.LogToConsole(message, color);
	}
}
