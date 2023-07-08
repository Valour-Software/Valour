namespace Valour.Server.Mapping;

public static class PermissionsNodeMapper
{
    public static PermissionsNode ToModel(this Valour.Database.PermissionsNode node)
    {
        if (node is null)
            return null;
        
        return new PermissionsNode()
        {
            Id = node.Id,
            PlanetId = node.PlanetId,
            Code = node.Code,
            Mask = node.Mask,
            RoleId = node.RoleId,
            TargetId = node.TargetId,
            TargetType = node.TargetType,
        };
    }
    
    public static Valour.Database.PermissionsNode ToDatabase(this PermissionsNode node)
    {
        if (node is null)
            return null;
        
        return new Valour.Database.PermissionsNode()
        {
            Id = node.Id,
            PlanetId = node.PlanetId,
            Code = node.Code,
            Mask = node.Mask,
            RoleId = node.RoleId,
            TargetId = node.TargetId,
            TargetType = node.TargetType
        };
    }
}