using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Sdk.Models;
using Valour.Server.Database;

namespace Valour.Tests.Client;

[Collection("ApiCollection")]
public class ChatWindowComponentTests : IClassFixture<LoginTestFixture>
{
    private readonly LoginTestFixture _fixture;

    public ChatWindowComponentTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetDefaultContent_WhenMentionedPlanetIsUnavailable_ReturnsFallbackContent()
    {
        var channel = new Channel(_fixture.Client)
        {
            Id = IdManager.Generate(),
            PlanetId = long.MaxValue,
            Name = "Deleted channel"
        };

        var content = await ChatWindowComponent.GetDefaultContent(channel);

        Assert.Equal(channel, content.Data);
        Assert.Equal("Deleted channel", content.Title);
        Assert.Equal(channel.PlanetId, content.PlanetId);
        Assert.Equal("./_content/Valour.Client/media/logo/logo-128.webp", content.Icon);
    }

    [Fact]
    public void ApplyMessageEditToDisplayedMessage_WhenReplyWasEdited_UpdatesReplyPreview()
    {
        var reply = new Message("old reply", null, null, 1, 2, _fixture.Client)
        {
            Id = IdManager.Generate(),
            Attachments = [],
            Mentions = []
        };
        var displayed = new Message("response", null, null, 3, 2, _fixture.Client)
        {
            Id = IdManager.Generate(),
            ReplyToId = reply.Id,
            ReplyTo = reply
        };
        var editedAt = DateTime.UtcNow;
        var edit = new Message("edited reply", null, null, 1, 2, _fixture.Client)
        {
            Id = reply.Id,
            EditedTime = editedAt,
            Attachments = [],
            Mentions = []
        };

        var target = ChatWindowComponent.ApplyMessageEditToDisplayedMessage(displayed, edit);

        Assert.Same(reply, target);
        Assert.Equal("edited reply", displayed.ReplyTo.Content);
        Assert.Equal(editedAt, displayed.ReplyTo.EditedTime);
    }

    [Fact]
    public void ApplyMessageEditToDisplayedMessage_WhenUnrelated_ReturnsNull()
    {
        var displayed = new Message("message", null, null, 1, 2, _fixture.Client)
        {
            Id = IdManager.Generate()
        };
        var edit = new Message("edit", null, null, 1, 2, _fixture.Client)
        {
            Id = IdManager.Generate()
        };

        Assert.Null(ChatWindowComponent.ApplyMessageEditToDisplayedMessage(displayed, edit));
        Assert.Equal("message", displayed.Content);
    }
}
