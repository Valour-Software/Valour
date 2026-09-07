using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Valour.Client.Components.Utility.EmojiMart;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class MentionSearchTests
{
    [Fact]
    public async Task OverlappingSearches_OnlyNewestResultIsPublishedAndKeysAreUnique()
    {
        var runtime = new SearchRuntime();
        using var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(runtime).BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var selector = CreateSelector(runtime);
        await renderer.Dispatcher.InvokeAsync(() => renderer.AttachAsync(selector));
        selector.Visible = true;
        selector.Mode = ':';

        var older = renderer.Dispatcher.InvokeAsync(() => selector.SetText(":think"));
        var newer = renderer.Dispatcher.InvokeAsync(() => selector.SetText(":thinking"));
        runtime.Searches["thinking"].SetResult([new() { Id = "thinking_face" }, new() { Id = "thinking_face" }]);
        await newer;
        runtime.Searches["think"].SetResult([new() { Id = "old_result" }]);
        await older;

        var matches = (IList)typeof(MentionSelectComponent).GetField("_matches", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(selector)!;
        Assert.Single(matches.Cast<object>());
        var match = matches[0]!;
        Assert.Equal("thinking_face", match.GetType().GetProperty("Key")!.GetValue(match));
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task ClosingSelectorDuringSearch_DiscardsPendingResults()
    {
        var runtime = new SearchRuntime();
        using var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(runtime).BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var selector = CreateSelector(runtime);
        await renderer.Dispatcher.InvokeAsync(() => renderer.AttachAsync(selector));
        selector.Visible = true;
        selector.Mode = ':';
        var pending = renderer.Dispatcher.InvokeAsync(() => selector.SetText(":think"));
        await renderer.Dispatcher.InvokeAsync(() => selector.SetVisible(false));
        runtime.Searches["think"].SetResult([new() { Id = "thinking_face" }]);
        await pending;
        Assert.False(selector.Visible);
        var matches = (IList)typeof(MentionSelectComponent).GetField("_matches", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(selector)!;
        Assert.Empty(matches);
        Assert.Empty(renderer.Errors);
    }

    private static MentionSelectComponent CreateSelector(SearchRuntime runtime)
    {
        var picker = new EmojiMart();
        typeof(EmojiMart).GetProperty("JsRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(picker, runtime);
        var input = new InputComponent();
        typeof(InputComponent).GetField("_emojis", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(input, picker);
        return new MentionSelectComponent { ChannelComponent = new ChatWindowComponent { Channel = new Channel(new Valour.Sdk.Client.ValourClient("http://localhost:1")), InputComponent = input } };
    }

#pragma warning disable BL0006
    private sealed class TestRenderer : Renderer
    {
        public List<Exception> Errors = [];
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public TestRenderer(IServiceProvider services) : base(services, services.GetRequiredService<ILoggerFactory>()) { }
        public Task AttachAsync(IComponent component) => RenderRootComponentAsync(AssignRootComponentId(component));
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => Errors.Add(exception);
    }
#pragma warning restore BL0006

    private sealed class SearchRuntime : IJSRuntime, IJSObjectReference
    {
        public Dictionary<string, TaskCompletionSource<EmojiClickEvent[]>> Searches = new();
        public async ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            if (identifier == "import") return (T)(object)this;
            if (identifier == "search")
            {
                var completion = new TaskCompletionSource<EmojiClickEvent[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                Searches[(string)args![0]!] = completion;
                return (T)(object)await completion.Task;
            }
            return default!;
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<T>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
