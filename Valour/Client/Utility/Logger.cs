namespace Valour.Client.Utility;

public static class Logger
{
	public static Pages.Index IndexPage { get; set; }

	public static async Task Log(string message, string color = null)
	{
		await IndexPage.LogToConsole(message, color);
	}
}
