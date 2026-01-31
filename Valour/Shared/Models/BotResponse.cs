namespace Valour.Shared.Models;

/// <summary>
/// Response model for bot operations
/// </summary>
public class BotResponse
{
    /// <summary>
    /// The ID of the bot user
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The name of the bot
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The tag (discriminator) of the bot
    /// </summary>
    public string Tag { get; set; }

    /// <summary>
    /// The bot's token - only included on creation or regeneration
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// The bot's status
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// True if the bot has a custom avatar
    /// </summary>
    public bool HasCustomAvatar { get; set; }

    /// <summary>
    /// True if the bot has an animated avatar
    /// </summary>
    public bool HasAnimatedAvatar { get; set; }

    /// <summary>
    /// When the bot was created
    /// </summary>
    public DateTime TimeJoined { get; set; }

    /// <summary>
    /// Creates a BotResponse from a User model
    /// </summary>
    public static BotResponse FromUser(ISharedUser user, string token = null)
    {
        return new BotResponse
        {
            Id = user.Id,
            Name = user.Name,
            Tag = user.Tag,
            Token = token,
            Status = user.Status,
            HasCustomAvatar = user.HasCustomAvatar,
            HasAnimatedAvatar = user.HasAnimatedAvatar,
            TimeJoined = user.TimeJoined
        };
    }
}
