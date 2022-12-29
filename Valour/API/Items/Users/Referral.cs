using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Api.Items.Users;

public class Referral : ISharedReferral
{
    public long UserId { get; set; }
    public long ReferrerId { get; set; }
}

