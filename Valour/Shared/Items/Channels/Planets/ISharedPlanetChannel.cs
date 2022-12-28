using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets;

namespace Valour.Shared.Items.Channels.Planets;

public interface ISharedPlanetChannel : ISharedChannel, ISharedPlanetItem, ISharedPermissionsTarget
{
    string Name { get; set; }
    long? ParentId { get; set; }
    int Position { get; set; }
    string Description { get; set; }
    bool InheritsPerms { get; set; }
}
