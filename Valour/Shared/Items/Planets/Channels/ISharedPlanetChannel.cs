using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Channels;

public interface ISharedPlanetChannel : ISharedChannel
{
    ulong PlanetId { get; set; }
    ulong? ParentId { get; set; }
}
