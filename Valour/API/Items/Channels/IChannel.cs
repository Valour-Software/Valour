namespace Valour.Api.Items.Channels;
public interface IChannel
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Task Open();
}
