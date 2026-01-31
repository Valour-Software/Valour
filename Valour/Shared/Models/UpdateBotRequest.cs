namespace Valour.Shared.Models;

/// <summary>
/// Request model for updating an existing bot
/// </summary>
public class UpdateBotRequest
{
    /// <summary>
    /// The new status for the bot
    /// </summary>
    public string Status { get; set; }
}
