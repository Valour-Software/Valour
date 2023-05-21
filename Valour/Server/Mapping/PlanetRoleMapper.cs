namespace Valour.Server.Mapping;

public static class PlanetRoleMapper
{
    public static PlanetRole ToModel(this Valour.Database.PlanetRole role)
    {
        if (role is null)
            return null;
        
        return new PlanetRole()
        {
            Id = role.Id,
            PlanetId = role.PlanetId,
            Position = role.Position,
            IsDefault = role.IsDefault,
            Permissions = role.Permissions,
            ChatPermissions = role.ChatPermissions,
            CategoryPermissions = role.CategoryPermissions,
            VoicePermissions = role.VoicePermissions,
            Red = role.Red,
            Green = role.Green,
            Blue = role.Blue,
            Bold = role.Bold,
            Italics = role.Italics,
            Name = role.Name,
        };
    }
    
    public static Valour.Database.PlanetRole ToDatabase(this PlanetRole role)
    {
        if (role is null)
            return null;
        
        return new Valour.Database.PlanetRole()
        {
            Id = role.Id,
            PlanetId = role.PlanetId,
            Position = role.Position,
            Permissions = role.Permissions,
            ChatPermissions = role.ChatPermissions,
            CategoryPermissions = role.CategoryPermissions,
            VoicePermissions = role.VoicePermissions,
            Red = role.Red,
            Green = role.Green,
            Blue = role.Blue,
            Bold = role.Bold,
            Italics = role.Italics,
            Name = role.Name,
            IsDefault = role.IsDefault,
        };
    }
}