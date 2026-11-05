using Valour.Client.Messages;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class MessageInputTextTests
{
    [Fact]
    public void Split_TurnsMentionsAndCustomEmojiIntoSegments()
    {
        var client = new ValourClient("https://valour.test/");
        var planet = new Planet(client) { Id = 7_000_000_000_000_001 };
        const long memberId = 12_638_576_793_878_528;
        planet.Members.Put(new PlanetMember(client) { Id = memberId, PlanetId = planet.Id, Nickname = "Nova" });

        var segments = MessageInputText.Split(
            $"hi «@m-{memberId}» look «e-:wave:~42»\nbye «@c-5»",
            planet,
            client);

        Assert.Equal(
        [
            new MessageInputSegment(MessageInputText.TextKind, "hi "),
            new MessageInputSegment(MessageInputText.MentionKind, $"«@m-{memberId}»", "@Nova"),
            new MessageInputSegment(MessageInputText.TextKind, " look "),
            new MessageInputSegment(MessageInputText.EmojiKind, "«e-:wave:~42»", ":wave:", ISharedPlanetEmoji.GetCdnUrl(planet.Id, 42)),
            new MessageInputSegment(MessageInputText.TextKind, "\nbye "),
            new MessageInputSegment(MessageInputText.MentionKind, "«@c-5»", "#"),
        ], segments);
    }

    [Fact]
    public void Split_KeepsCustomEmojiAsTextWithoutAPlanet()
    {
        var segments = MessageInputText.Split("«e-:wave:~42» «@u-9»", null, null);

        Assert.Equal(
        [
            new MessageInputSegment(MessageInputText.TextKind, "«e-:wave:~42» "),
            new MessageInputSegment(MessageInputText.MentionKind, "«@u-9»", "@"),
        ], segments);
    }

    [Theory]
    [InlineData("«@m-123»", MentionType.PlanetMember, 123L)]
    [InlineData("«@u-9»", MentionType.User, 9L)]
    [InlineData("«@r-4»", MentionType.Role, 4L)]
    [InlineData("«@c-12638576793878528»", MentionType.Channel, 12638576793878528L)]
    public void TryParseMention_ReadsWholeTokens(string token, MentionType type, long id)
    {
        Assert.True(MessageInputText.TryParseMention(token, out var mention));
        Assert.Equal(type, mention.Type);
        Assert.Equal(id, mention.TargetId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("«@x-1»")]
    [InlineData(" «@m-1»")]
    [InlineData("«@m-1» ")]
    [InlineData("«e-:wave:~42»")]
    public void TryParseMention_RejectsOtherText(string token)
    {
        Assert.False(MessageInputText.TryParseMention(token, out _));
    }
}
