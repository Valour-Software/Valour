using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items;

namespace Valour.Database;

public abstract class Item : ISharedItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Database items should never be directly returned to the client,
    /// and they don't really have a node name. 
    /// </summary>
    public string NodeName => "Database";
}

