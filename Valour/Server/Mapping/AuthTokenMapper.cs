namespace Valour.Server.Mapping;

public static class AuthTokenMapper
{
    public static AuthToken ToModel(this Valour.Database.AuthToken token)
    {
        if (token is null)
            return null;
        
        return new AuthToken()
        {
            Id = token.Id,
            TokenType = token.TokenType,
            AppId = token.AppId,
            UserId = token.UserId,
            Scope = token.Scope,
            TimeCreated = token.TimeCreated,
            TimeExpires = token.TimeExpires,
            Audience = token.Audience,
            IssuedAddress = token.IssuedAddress
        };
    }
    
    public static Valour.Database.AuthToken ToDatabase(this AuthToken token)
    {
        if (token is null)
            return null;
        
        return new Valour.Database.AuthToken()
        {
            Id = token.Id,
            TokenType = token.TokenType,
            AppId = token.AppId,
            UserId = token.UserId,
            Scope = token.Scope,
            TimeCreated = token.TimeCreated,
            TimeExpires = token.TimeExpires,
            Audience = token.Audience,
            IssuedAddress = token.IssuedAddress
        };
    }
}
