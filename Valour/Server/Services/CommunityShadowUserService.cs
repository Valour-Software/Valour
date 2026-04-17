using System.Globalization;

namespace Valour.Server.Services;

public class CommunityShadowUserService
{
    private readonly ValourDb _db;
    private readonly ILogger<CommunityShadowUserService> _logger;

    public CommunityShadowUserService(
        ValourDb db,
        ILogger<CommunityShadowUserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureShadowUserAsync(CommunityTokenPayload payload)
    {
        var authority = "official";
        var authorityUserId = payload.UserId.ToString(CultureInfo.InvariantCulture);
        var existing = await _db.Users.FindAsync(payload.UserId);

        if (existing is null)
        {
            existing = new Valour.Database.User
            {
                Id = payload.UserId,
                IsShadowUser = true,
                IdentityAuthority = authority,
                IdentityAuthorityUserId = authorityUserId
            };

            _db.Users.Add(existing);
        }
        else if (!existing.IsShadowUser &&
                 (!string.Equals(existing.IdentityAuthority, authority, StringComparison.OrdinalIgnoreCase) ||
                  !string.Equals(existing.IdentityAuthorityUserId, authorityUserId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Cannot materialize community shadow user {payload.UserId} because a non-shadow user with the same ID already exists.");
        }

        var snapshot = payload.User;

        existing.IsShadowUser = true;
        existing.IdentityAuthority = authority;
        existing.IdentityAuthorityUserId = authorityUserId;
        existing.Name = snapshot.Name;
        existing.Tag = snapshot.Tag;
        existing.TimeJoined = snapshot.TimeJoined;
        existing.TimeLastActive = snapshot.TimeLastActive;
        existing.HasCustomAvatar = snapshot.HasCustomAvatar;
        existing.HasAnimatedAvatar = snapshot.HasAnimatedAvatar;
        existing.Bot = snapshot.Bot;
        existing.Disabled = snapshot.Disabled;
        existing.ValourStaff = snapshot.ValourStaff;
        existing.Status = snapshot.Status;
        existing.UserStateCode = snapshot.UserStateCode;
        existing.IsMobile = snapshot.IsMobile;
        existing.Compliance = snapshot.Compliance;
        existing.SubscriptionType = snapshot.SubscriptionType;
        existing.PriorName = snapshot.PriorName;
        existing.NameChangeTime = snapshot.NameChangeTime;
        existing.Version = snapshot.Version;
        existing.TutorialState = snapshot.TutorialState;
        existing.OwnerId = snapshot.OwnerId;
        existing.StarColor1 = snapshot.StarColor1;
        existing.StarColor2 = snapshot.StarColor2;

        var profile = await _db.UserProfiles.FindAsync(payload.UserId);
        if (profile is null)
        {
            profile = new Valour.Database.UserProfile
            {
                Id = payload.UserId,
                Headline = "Community account",
                Bio = string.Empty,
                BorderColor = "#fff",
                AnimatedBorder = false
            };

            _db.UserProfiles.Add(profile);
        }

        await _db.SaveChangesAsync();

        _logger.LogDebug(
            "Ensured shadow user {UserId} for authority {Authority} on community node",
            payload.UserId,
            authority);
    }
}
