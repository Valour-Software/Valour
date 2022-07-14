using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

public interface ISharedReferral
{
    long UserId { get; set; }
    long ReferrerId { get; set; }
}

