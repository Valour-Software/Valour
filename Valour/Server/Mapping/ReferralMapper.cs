namespace Valour.Server.Mapping;

public static class ReferralMapper
{
    public static Referral ToModel(this Valour.Database.Referral referral)
    {
        if (referral is null)
            return null;
        
        return new Referral()
        {
            UserId = referral.UserId,
            ReferrerId = referral.ReferrerId
        };
    }
    
    public static Valour.Database.Referral ToDatabase(this Referral referral)
    {
        if (referral is null)
            return null;
        
        return new Valour.Database.Referral()
        {
            UserId = referral.UserId,
            ReferrerId = referral.ReferrerId
        };
    }
}