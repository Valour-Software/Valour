namespace Valour.Sdk.Models;

/// <summary>
/// Rather than using multiple API calls to get the initial data for a planet,
/// we can use this class to store all the data we need in one go.
/// </summary>
public class InitialPlanetData
{
    /// <summary>
    /// Channels the user has access to
    /// </summary>
    public List<Channel> Channels { get; set; }
    
    /// <summary>
    /// All the roles in the planet
    /// </summary>
    public List<PlanetRole> Roles { get; set; }

    /// <summary>
    /// Custom emojis available in the planet
    /// </summary>
    public List<PlanetEmoji> Emojis { get; set; }

    /// <summary>
    /// Rules configured for the planet.
    /// </summary>
    public List<PlanetRule> Rules { get; set; }

    /// <summary>
    /// Users currently in each voice channel (channelId -> userIds)
    /// </summary>
    public Dictionary<long, List<long>> VoiceParticipants { get; set; }
}
