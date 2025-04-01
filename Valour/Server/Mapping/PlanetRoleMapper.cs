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
            IsAdmin = role.IsAdmin,
            PlanetId = role.PlanetId,
            Position = role.Position,
            IsDefault = role.IsDefault,
            Permissions = role.Permissions,
            ChatPermissions = role.ChatPermissions,
            CategoryPermissions = role.CategoryPermissions,
            VoicePermissions = role.VoicePermissions,
            Color = role.Color,
            Bold = role.Bold,
            Italics = role.Italics,
            Name = role.Name,
            AnyoneCanMention = role.AnyoneCanMention,
            FlagBitIndex = role.FlagBitIndex
        };
    }
    
    public static Valour.Database.PlanetRole ToDatabase(this PlanetRole role)
    {
        if (role is null)
            return null;
        
        return new Valour.Database.PlanetRole()
        {
            Id = role.Id,
            IsAdmin = role.IsAdmin,
            PlanetId = role.PlanetId,
            Position = role.Position,
            Permissions = role.Permissions,
            ChatPermissions = role.ChatPermissions,
            CategoryPermissions = role.CategoryPermissions,
            VoicePermissions = role.VoicePermissions,
            Color = role.Color,
            Bold = role.Bold,
            Italics = role.Italics,
            Name = role.Name,
            IsDefault = role.IsDefault,
            AnyoneCanMention = role.AnyoneCanMention,
            FlagBitIndex = role.FlagBitIndex
        };
    }
}