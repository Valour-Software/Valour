using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Api.Models;
using Valour.Shared.Models;

namespace Valour.Api.Models;

[JsonDerivedType(typeof(PlanetChannel), typeDiscriminator: nameof(PlanetChannel))]
public abstract class Channel : LiveModel, IChannel, ISharedChannel
{
    public string Name { get; set; }
    public string Description { get; set; }
    
    public abstract Task Open();
    public abstract Task Close();
}
