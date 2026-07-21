using Microsoft.AspNetCore.Components;
using Valour.Client.Components.DockWindows;

namespace Valour.Tests.Client;

public class WindowTabTests
{
    [Fact]
    public async Task SetContent_ReplacesContentAndUpdatesOwnership()
    {
        var original = new TestWindowContent("original");
        var replacement = new TestWindowContent("replacement");
        var tab = new WindowTab(original);

        await tab.SetContent(replacement);

        Assert.Same(replacement, tab.Content);
        Assert.Same(tab, replacement.Tab);
        Assert.Same(tab, original.Tab);
    }

    [Fact]
    public async Task SetContent_WithSameInstance_IsANoOp()
    {
        var content = new TestWindowContent("content");
        var tab = new WindowTab(content);

        await tab.SetContent(content);

        Assert.Same(content, tab.Content);
        Assert.Same(tab, content.Tab);
    }

    [Fact]
    public async Task SetContent_SequentialReplacements_KeepLatestContent()
    {
        var tab = new WindowTab(new TestWindowContent("original"));
        var first = new TestWindowContent("first");
        var second = new TestWindowContent("second");

        await tab.SetContent(first);
        await tab.SetContent(second);

        Assert.Same(second, tab.Content);
        Assert.Same(tab, second.Tab);
    }

    [Fact]
    public async Task NavigateToHistory_RestoresEarlierAndLaterContent()
    {
        var original = new TestWindowContent("original");
        var first = new TestWindowContent("first");
        var second = new TestWindowContent("second");
        var tab = new WindowTab(original);
        await tab.SetContent(first);
        await tab.SetContent(second);

        Assert.True(await tab.NavigateToHistoryAsync(first.Id));
        Assert.Same(first, tab.Content);

        Assert.True(await tab.NavigateToHistoryAsync(second.Id));
        Assert.Same(second, tab.Content);
    }

    [Fact]
    public async Task NewNavigationAfterBack_DiscardsForwardHistory()
    {
        var original = new TestWindowContent("original");
        var first = new TestWindowContent("first");
        var discarded = new TestWindowContent("discarded");
        var replacement = new TestWindowContent("replacement");
        var tab = new WindowTab(original);
        await tab.SetContent(first);
        await tab.SetContent(discarded);
        await tab.NavigateToHistoryAsync(first.Id);

        await tab.SetContent(replacement);

        Assert.False(await tab.NavigateToHistoryAsync(discarded.Id));
        Assert.Same(replacement, tab.Content);
    }

    private sealed class TestWindowContent(string text) : WindowContent
    {
        public override RenderFragment RenderContent => builder => builder.AddContent(0, text);
    }
}
