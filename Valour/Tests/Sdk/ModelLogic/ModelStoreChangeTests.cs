using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models;

namespace Valour.Tests.Sdk.ModelLogic;

public class ModelStoreChangeTests
{
    private static Message NewMessage(string content, byte[] envelope) =>
        new((ValourClient)null!)
        {
            Id = 1,
            Content = content,
            Envelope = envelope,
            PreviewUrls = ["https://example.com"],
            CustomEmojiIds = [1],
            SearchTerms = [1, 2]
        };

    private static PlanetRole NewRole(long id, uint position) =>
        new((ValourClient)null!) { Id = id, Position = position, Name = "role" + id };

    [Fact]
    public void Put_IdenticalContent_RaisesNoUpdate()
    {
        var store = new ModelStore<Message, long>();
        var updates = 0;
        store.ModelUpdated += _ => updates++;

        store.Put(NewMessage("a", [1, 2, 3]));
        store.Put(NewMessage("a", [1, 2, 3]));

        Assert.Equal(0, updates);
    }

    [Fact]
    public void Put_ChangedContentAndEnvelope_ReportsBothChanges()
    {
        var store = new ModelStore<Message, long>();
        store.Put(NewMessage("a", [1, 2, 3]));

        string? oldContent = null;
        string? newContent = null;
        var envelopeChanged = false;
        store.ModelUpdated += e =>
        {
            e.Changes!.On(x => x.Content, out oldContent, out newContent);
            envelopeChanged = e.Changes.On(x => x.Envelope);
        };

        store.Put(NewMessage("b", [1, 2, 4]));

        Assert.Equal("a", oldContent);
        Assert.Equal("b", newContent);
        Assert.True(envelopeChanged);
        Assert.Equal("b", store.Get(1)!.Content);
        Assert.Equal(new byte[] { 1, 2, 4 }, store.Get(1)!.Envelope);
    }

    [Fact]
    public void SortedStore_InsertsAndRepositionsByPosition()
    {
        var store = new SortedModelStore<PlanetRole, long>();
        foreach (var (id, position) in new[] { (1L, 5u), (2L, 1u), (3L, 3u), (4L, 9u), (5L, 2u) })
            store.Put(NewRole(id, position));

        Assert.Equal(new long[] { 2, 5, 3, 1, 4 }, store.Select(x => x.Id).ToArray());

        store.Put(NewRole(1, 0));
        Assert.Equal(new long[] { 1, 2, 5, 3, 4 }, store.Select(x => x.Id).ToArray());

        store.Remove(3);
        Assert.Equal(new long[] { 1, 2, 5, 4 }, store.Select(x => x.Id).ToArray());
    }
}
