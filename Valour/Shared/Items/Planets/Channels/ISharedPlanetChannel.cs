using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Channels;

public interface ISharedPlanetChannel : ISharedChannel, ISharedPlanetItem
{
    long? ParentId { get; set; }
}
