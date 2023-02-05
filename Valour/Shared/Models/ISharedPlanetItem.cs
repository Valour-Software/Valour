using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Models;

/// <summary>
/// Planet items are items which are owned by a planet
/// </summary>
public interface ISharedPlanetItem : ISharedItem
{
    long PlanetId { get; set; }
}
