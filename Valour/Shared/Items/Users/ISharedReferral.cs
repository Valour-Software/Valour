using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

public interface ISharedReferral
{
    ulong UserId { get; set; }
    ulong ReferrerId { get; set; }
}

