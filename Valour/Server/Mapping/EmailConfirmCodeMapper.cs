namespace Valour.Server.Mapping;

public static class EmailConfirmCodeMapper
{
    public static EmailConfirmCode ToModel(this Valour.Database.EmailConfirmCode code)
    {
        if (code is null)
            return null;
        
        return new EmailConfirmCode()
        {
            UserId = code.UserId,
            Code = code.Code
        };
    }
    
    public static Valour.Database.EmailConfirmCode ToDatabase(this EmailConfirmCode code)
    {
        if (code is null)
            return null;
        
        return new Valour.Database.EmailConfirmCode()
        {
            UserId = code.UserId,
            Code = code.Code
        };
    }
}