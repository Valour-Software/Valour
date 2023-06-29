namespace Valour.Server.Mapping;

public static class UserEmailMapper
{
    public static UserPrivateInfo ToModel(this Valour.Database.UserPrivateInfo email)
    {
        if (email is null)
            return null;
        
        return new UserPrivateInfo()
        {
            Email = email.Email,
            Verified = email.Verified,
            UserId = email.UserId
        };
    }
    
    public static Valour.Database.UserPrivateInfo ToDatabase(this UserPrivateInfo privateInfo)
    {
        if (privateInfo is null)
            return null;
        
        return new Valour.Database.UserPrivateInfo()
        {
            Email = privateInfo.Email,
            Verified = privateInfo.Verified,
            UserId = privateInfo.UserId
        };
    }
}