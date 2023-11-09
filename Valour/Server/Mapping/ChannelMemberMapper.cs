namespace Valour.Server.Mapping;

public static class ChannelMemberMapper
{
    public static ChannelMember ToModel(this Valour.Database.ChannelMember member)
    {
        if (member is null)
            return null;

        return new ChannelMember()
        {
            Id = member.Id,
            ChannelId = member.ChannelId,
            UserId = member.UserId
        };
    }
    
    public static Valour.Database.ChannelMember ToDatabase(this ChannelMember member)
    {
        if (member is null)
            return null;

        return new Valour.Database.ChannelMember()
        {
            Id = member.Id,
            ChannelId = member.ChannelId,
            UserId = member.UserId
        };
    }
}