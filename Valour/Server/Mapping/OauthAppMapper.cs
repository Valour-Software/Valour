namespace Valour.Server.Mapping;

public static class OauthAppMapper
{
    public static OauthApp ToModel(this Valour.Database.OauthApp app)
    {
        if (app is null)
            return null;
        
        return new OauthApp()
        {
            Id = app.Id,
            Secret = app.Secret,
            OwnerId = app.OwnerId,  
            Uses = app.Uses,
            ImageUrl = app.ImageUrl,
            Name = app.Name,
            RedirectUrl = app.RedirectUrl
        };
    }
    
    public static Valour.Database.OauthApp ToDatabase(this Valour.Database.OauthApp app)
    {
        if (app is null)
            return null;
        
        return new Valour.Database.OauthApp()
        {
            Id = app.Id,
            Secret = app.Secret,
            OwnerId = app.OwnerId,
            Uses = app.Uses,
            ImageUrl = app.ImageUrl,
            Name = app.Name,
            RedirectUrl = app.RedirectUrl
        };
    }
}