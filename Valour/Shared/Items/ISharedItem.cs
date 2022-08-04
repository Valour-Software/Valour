
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

public interface ISharedItem
{
    long Id { get; set; }
    string NodeName { get; }

    string IdRoute { get; }
    string BaseRoute { get; }
}


