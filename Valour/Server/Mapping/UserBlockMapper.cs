namespace Valour.Server.Mapping;

public static class UserBlockMapper
{
    public static UserBlock ToModel(this Valour.Database.UserBlock block)
    {
        if (block is null)
            return null;

        return new UserBlock()
        {
            Id = block.Id,
            UserId = block.UserId,
            BlockedUserId = block.BlockedUserId,
            BlockType = block.BlockType,
            CreatedAt = block.CreatedAt
        };
    }

    public static Valour.Database.UserBlock ToDatabase(this UserBlock block)
    {
        if (block is null)
            return null;

        return new Valour.Database.UserBlock()
        {
            Id = block.Id,
            UserId = block.UserId,
            BlockedUserId = block.BlockedUserId,
            BlockType = block.BlockType,
            CreatedAt = block.CreatedAt
        };
    }
}
