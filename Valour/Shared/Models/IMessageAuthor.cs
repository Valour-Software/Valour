namespace Valour.Shared.Models;

public interface IMessageAuthor : ISharedModel<long>
{
    public string Name { get; }
    public string GetAvatar(AvatarFormat format = AvatarFormat.WebpAnimated256);
    public string GetFailedAvatar();
}