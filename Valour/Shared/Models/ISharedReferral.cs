using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

public interface ISharedReferral
{
    long UserId { get; set; }
    long ReferrerId { get; set; }
}

