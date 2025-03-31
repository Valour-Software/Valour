using System.Collections.Immutable;
using Valour.Shared.Collections;
using Valour.Shared.Models.Calls;

namespace Valour.Server.Models;

/// <summary>
/// The live call model represents a call that is currently active
/// </summary>
public class LiveCall
{
    public readonly SnapshotList<LiveCallParticipant> Participants = new();
    public long ChannelId { get; set; }
    public bool Initialized { get; set; }
}