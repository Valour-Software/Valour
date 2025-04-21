namespace Valour.Shared.Models;

/// <summary>
/// This information is used to give the client a summary of a planet that has
/// not yet been loaded.
/// </summary>
public class PlanetListInfo
{
    public long PlanetId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool HasCustomIcon { get; set; }
    public bool HasAnimatedIcon { get; set; }
    public bool Nsfw { get; set; }
    public int MemberCount { get; set; }
    public int Version { get; set; }
    
    // planet unread/read state
    public bool HasUnread { get; set; }
    public int UnreadNotificationCount { get; set; }
}