using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Shared.Models;

public interface ISharedPlanetChannel : ISharedChannel, ISharedPlanetItem, ISharedPermissionsTarget
{
    string Name { get; set; }
    long? ParentId { get; set; }
    int Position { get; set; }
    string Description { get; set; }
    bool InheritsPerms { get; set; }
}
