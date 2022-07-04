using System.Text.Json.Serialization;
using Valour.Shared.Items.Users;

namespace Valour.Api.Items.Users;

public class Referral : ISharedReferral
{
    public ulong UserId { get; set; }
    public ulong ReferrerId { get; set; }
}

