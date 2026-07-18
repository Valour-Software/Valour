using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Planet : ServerModel<long>, ISharedPlanet
{
    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The node this planet belongs to
    /// </summary>
    public string NodeName { get; set; } 

    /// <summary>
    /// True if the planet has a custom icon
    /// </summary>
    public bool HasCustomIcon { get; set; }
    
    /// <summary>
    /// True if the planet has an animated icon
    /// </summary>
    public bool HasAnimatedIcon { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    public bool Public { get; set; }

    /// <summary>
    /// If the server should show up on the discovery tab
    /// </summary>
    public bool Discoverable { get; set; }
    
    /// <summary>
    /// True if you probably shouldn't be on this server at work owo
    /// </summary>
    public bool Nsfw { get; set; }

    /// <summary>
    /// True when this planet stores media on its own infrastructure
    /// (bring-your-own-storage) rather than Valour's CDN
    /// </summary>
    public bool SelfHostedMedia { get; set; }

    /// <summary>
    /// True when this planet runs voice/video calls on its own LiveKit SFU
    /// (bring-your-own-voice) rather than Valour's voice backend
    /// </summary>
    public bool SelfHostedVoice { get; set; }

    /// <summary>
    /// True while a migration is in progress — the planet is read-only.
    /// </summary>
    public bool LockedForMigration { get; set; }

    /// <summary>
    /// The version of the planet. Used for cache busting.
    /// </summary>
    public int Version { get; set; }
    
    /// <summary>
    /// True if the planet has a custom background
    /// </summary>
    public bool HasCustomBackground { get; set; }

    /// <summary>
    /// True if the threads feed is enabled for this planet
    /// </summary>
    public bool EnableThreads { get; set; }

    /// <summary>
    /// True if this planet's threads can be browsed publicly without an account
    /// </summary>
    public bool PublicThreads { get; set; }

    /// <summary>
    /// The id of the single thread pinned to the top of this planet's feed, if any
    /// </summary>
    public long? PinnedThreadId { get; set; }

    /// <summary>
    /// True if the docs/wiki is enabled for this planet
    /// </summary>
    public bool EnableWiki { get; set; }

    /// <summary>
    /// True if this planet's docs can be read publicly without an account
    /// </summary>
    public bool PublicWiki { get; set; }

    /// <summary>
    /// The vanity name claimed for this planet's public docs site, if any
    /// </summary>
    public string Vanity { get; set; }

    public List<PlanetTag> Tags { get; set; } = new();
    
    public string GetIconUrl(IconFormat format = IconFormat.Webp256) =>
        ISharedPlanet.GetIconUrl(this, format);
}