
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

public interface ISharedItem
{
    ulong Id { get; set; }
    ItemType ItemType { get; }
    string Node { get; }
}


