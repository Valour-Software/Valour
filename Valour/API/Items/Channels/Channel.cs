using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Api.Items.Channels.Planets;
using Valour.Shared.Items.Channels;

namespace Valour.Api.Items.Channels;

[JsonDerivedType(typeof(PlanetChannel), typeDiscriminator: nameof(PlanetChannel))]
public abstract class Channel : Item, IChannel, ISharedChannel
{
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime TimeLastActive { get; set; }
    public string State { get; set; }

    public abstract Task Open();
    public abstract Task Close();
}
