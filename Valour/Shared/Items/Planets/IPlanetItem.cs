using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Items.Planets;

/// <summary>
/// Planet items are items which are owned by a planet
/// </summary>
public interface IPlanetItem
{
    [JsonInclude]
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }
}
