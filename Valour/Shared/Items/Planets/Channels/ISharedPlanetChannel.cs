using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Channels;

public interface ISharedPlanetChannel : ISharedChannel, ISharedPlanetItem
{
    string Name { get; set; }
    long? ParentId { get; set; }
    int Position { get; set; }
    string Description { get; set; }
    bool InheritsPerms { get; set; }
}
