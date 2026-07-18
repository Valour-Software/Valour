namespace Valour.Shared.Models;

/// <summary>
/// A complete, portable copy of a planet's data for migration between the
/// official network and community nodes. IDs are the global snowflake ids and
/// are preserved verbatim on import — migration keeps the planet, its channels,
/// roles, members, and messages at their original ids, so no id remapping is
/// needed. Referenced users are hub-global; the destination materializes shadow
/// user rows for any it doesn't already have.
/// </summary>
public class PlanetSnapshot
{
    public int ProtocolVersion { get; set; } = ValourFederation.ProtocolVersion;
    public DateTime CreatedAt { get; set; }
    public string SourceDomain { get; set; }

    public PlanetSnapshotPlanet Planet { get; set; }
    public List<PlanetSnapshotChannel> Channels { get; set; } = new();
    public List<PlanetSnapshotRole> Roles { get; set; } = new();
    public List<PlanetSnapshotPermNode> PermissionNodes { get; set; } = new();
    public List<PlanetSnapshotMember> Members { get; set; } = new();
    public List<PlanetSnapshotEmoji> Emojis { get; set; } = new();
    public List<PlanetSnapshotRule> Rules { get; set; } = new();
    public List<PlanetSnapshotBan> Bans { get; set; } = new();
    public List<PlanetSnapshotMessage> Messages { get; set; } = new();
    public List<PlanetSnapshotAttachment> Attachments { get; set; } = new();
    public List<PlanetSnapshotReaction> Reactions { get; set; } = new();
    public List<PlanetSnapshotMention> Mentions { get; set; } = new();

    /// <summary>
    /// Names of members/authors so the destination can build shadow users.
    /// </summary>
    public List<PlanetSnapshotUser> Users { get; set; } = new();
}

public class PlanetSnapshotUser
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string Tag { get; set; }
    public string SubscriptionType { get; set; }
}

public class PlanetSnapshotPlanet
{
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Public { get; set; }
    public bool Discoverable { get; set; }
    public bool Nsfw { get; set; }
    public bool HasCustomIcon { get; set; }
    public bool HasAnimatedIcon { get; set; }
    public bool HasCustomBackground { get; set; }
    public bool SelfHostedMedia { get; set; }
    public bool EnableThreads { get; set; }
    public bool PublicThreads { get; set; }
    public long? PinnedThreadId { get; set; }
    public int Version { get; set; }
}

public class PlanetSnapshotChannel
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public ChannelTypeEnum ChannelType { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public long? PlanetId { get; set; }
    public long? ParentId { get; set; }
    public uint RawPosition { get; set; }
    public bool InheritsPerms { get; set; }
    public bool IsDefault { get; set; }
    public bool Nsfw { get; set; }
    public long? AssociatedChatChannelId { get; set; }
    public int Version { get; set; }
}

public class PlanetSnapshotRole
{
    public long Id { get; set; }
    public int FlagBitIndex { get; set; }
    public bool IsAdmin { get; set; }
    public long PlanetId { get; set; }
    public uint Position { get; set; }
    public bool IsDefault { get; set; }
    public long Permissions { get; set; }
    public long ChatPermissions { get; set; }
    public long CategoryPermissions { get; set; }
    public long VoicePermissions { get; set; }
    public string Color { get; set; }
    public bool Bold { get; set; }
    public bool Italics { get; set; }
    public string Name { get; set; }
    public bool AnyoneCanMention { get; set; }
    public int Version { get; set; }
}

public class PlanetSnapshotPermNode
{
    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long Code { get; set; }
    public long Mask { get; set; }
    public long RoleId { get; set; }
    public long TargetId { get; set; }
    public ChannelTypeEnum TargetType { get; set; }
}

public class PlanetSnapshotMember
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long PlanetId { get; set; }
    public string Nickname { get; set; }
    public string MemberAvatar { get; set; }
    public long? DismissedPinThreadId { get; set; }
    public DateTime TimeLastConnected { get; set; }
    public long Rf0 { get; set; }
    public long Rf1 { get; set; }
    public long Rf2 { get; set; }
    public long Rf3 { get; set; }
}

public class PlanetSnapshotEmoji
{
    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long CreatorUserId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PlanetSnapshotRule
{
    public long Id { get; set; }
    public long PlanetId { get; set; }
    public uint Position { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
}

public class PlanetSnapshotBan
{
    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long IssuerId { get; set; }
    public long TargetId { get; set; }
    public string Reason { get; set; }
    public DateTime TimeCreated { get; set; }
    public DateTime? TimeExpires { get; set; }
}

public class PlanetSnapshotMessage
{
    public long Id { get; set; }
    public long? PlanetId { get; set; }
    public long? ReplyToId { get; set; }
    public long AuthorUserId { get; set; }
    public long? AuthorMemberId { get; set; }
    public string Content { get; set; }
    public DateTime TimeSent { get; set; }
    public long ChannelId { get; set; }
    public DateTime? EditedTime { get; set; }
}

public class PlanetSnapshotAttachment
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public int SortOrder { get; set; }
    public MessageAttachmentType Type { get; set; }
    public string CdnBucketItemId { get; set; }
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Inline { get; set; }
    public bool Missing { get; set; }
    public string Data { get; set; }
    public string OpenGraphData { get; set; }
    public bool PlanetHosted { get; set; }
    public string ReportedSha256 { get; set; }
}

public class PlanetSnapshotReaction
{
    public long Id { get; set; }
    public string Emoji { get; set; }
    public long MessageId { get; set; }
    public long AuthorUserId { get; set; }
    public long? AuthorMemberId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PlanetSnapshotMention
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public int SortOrder { get; set; }
    public MentionType Type { get; set; }
    public long TargetId { get; set; }
}
