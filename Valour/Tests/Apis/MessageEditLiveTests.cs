namespace Valour.Tests.Apis;

/// <summary>
/// Message edits go through the same content checks as new posts: the author
/// must still be able to post, and content the edit adds is validated.
/// </summary>
[Collection("ApiCollection")]
public class MessageEditLiveTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;

    private Valour.Sdk.Models.Planet _planet = null!;
    private Valour.Sdk.Models.Channel _channel = null!;

    public MessageEditLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var create = await new Valour.Sdk.Models.Planet(_fixture.Client)
        {
            Name = $"Msg Edit {Guid.NewGuid().ToString()[..8]}",
            Description = "Message edit test planet",
        }.CreateAsync();
        Assert.True(create.Success, create.Message);

        _planet = await _fixture.Client.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        _channel = await _planet.FetchPrimaryChatChannelAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<Valour.Sdk.Models.Message> SendAsync(string content)
    {
        await _planet.EnsureReadyAsync();

        var message = new Valour.Sdk.Models.Message(_fixture.Client)
        {
            Content = content,
            ChannelId = _channel.Id,
            PlanetId = _planet.Id,
            AuthorUserId = _fixture.Client.Me.Id,
            AuthorMemberId = _planet.MyMember?.Id,
            Fingerprint = Guid.NewGuid().ToString(),
        };

        var result = await _fixture.Client.MessageService.SendMessage(message);
        Assert.True(result.Success, result.Message);
        return result.Data;
    }

    // The send response is not bound to the client, so edits go through a bound copy
    private Valour.Sdk.Models.Message ForEdit(Valour.Sdk.Models.Message sent, string content) =>
        new(_fixture.Client)
        {
            Id = sent.Id,
            Content = content,
            ChannelId = sent.ChannelId,
            PlanetId = sent.PlanetId,
            AuthorUserId = sent.AuthorUserId,
            AuthorMemberId = sent.AuthorMemberId,
            Fingerprint = sent.Fingerprint,
        };

    [Fact]
    public async Task Edit_ByAuthor_Succeeds()
    {
        var message = ForEdit(await SendAsync("before edit"), "after edit");
        var result = await message.UpdateAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal("after edit", result.Data.Content);
    }

    [Fact]
    public async Task Edit_AddingInvalidCustomEmoji_IsRejected()
    {
        var message = ForEdit(await SendAsync("plain message"), "now with «e-:not_here:~1234567»");
        var result = await message.UpdateAsync();

        Assert.False(result.Success);
    }
}
