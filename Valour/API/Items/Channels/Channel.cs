using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Api.Items.Planets.Channels;

namespace Valour.Api.Items.Channels;

[JsonDerivedType(typeof(PlanetChannel), typeDiscriminator: nameof(PlanetChannel))]
public class Channel : Item
{
    public DateTime TimeLastActive { get; set; }
    public string State { get; set; }
}
