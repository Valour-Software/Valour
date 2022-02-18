
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

public abstract class Item
{
    [Key]
    [JsonInclude]
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public abstract ItemType ItemType { get; }

    // Override equality so that equal ID is seen as the same object.

    public override bool Equals(object obj)
    {
        Item con = obj as Item;

        if (con != null)
            return con.Id == this.Id;

        return false;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}


