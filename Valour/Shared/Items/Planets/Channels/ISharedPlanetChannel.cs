using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Channels;

public interface ISharedPlanetChannel
{
    ulong PlanetId { get; set; }
    ulong? ParentId { get; set; }

    // Inherited from ISharedChannel
    ulong Id { get; set; }
    string Name { get; set; }
    int Position { get; set; }
    string Description { get; set; }
}
