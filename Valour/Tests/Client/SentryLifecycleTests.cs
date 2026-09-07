using System.Reflection;
using Microsoft.JSInterop;
using Valour.Client.Components.Utility;
using Valour.Client.Components.Utility.EmojiMart;

namespace Valour.Tests.Client;

public class SentryLifecycleTests
{
    [Fact]
    public async Task FadeClosedDuringImport_ReleasesLateModuleWithoutAnimating()
    {
        var runtime = new DeferredRuntime();
        var fade = new TestFade(runtime);
        var render = fade.RenderAsync();
        await fade.DisposeAsync();
        runtime.Import.SetResult(runtime.Module);
        await render;
        Assert.DoesNotContain("fadeIn", runtime.Module.Calls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
        await fade.DisposeAsync();
        Assert.Equal(1, runtime.Module.DisposeCalls);
    }

    [Fact]
    public async Task FadeOutWithoutInitialization_CompletesCallbackExactlyOnce()
    {
        var fade = new TestFade(new DeferredRuntime());
        var callbacks = 0;
        fade.OnFadeOut = () => { callbacks++; return Task.CompletedTask; };
        await fade.FadeOut();
        await fade.FadeOut();
        Assert.Equal(1, callbacks);
    }

    [Fact]
    public async Task ColorPickerClosedDuringInit_KeepsCallbackAliveUntilDestroy()
    {
        var runtime = new DeferredRuntime();
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.HoldInit = true;
        var picker = new TestColorPicker(runtime);
        var render = picker.RenderAsync();
        await runtime.Module.InitStarted.Task;
        await picker.DisposeAsync();
        Assert.Equal(0, runtime.Module.DisposeCalls);
        runtime.Module.InitCompleted.SetResult();
        await render;
        Assert.True(runtime.Module.CallbackAliveAtDestroy);
        Assert.Equal(1, runtime.Module.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() => runtime.Module.ColorCallback!.Value);
    }

    [Fact]
    public async Task EmojiSearchClosedDuringImport_ReturnsNoResultsAndReleasesModule()
    {
        var runtime = new DeferredRuntime();
        var picker = new EmojiMart();
        Inject(picker, "JsRuntime", runtime);
        var search = picker.SearchAsync("thinking");
        await ((IAsyncDisposable)picker).DisposeAsync();
        runtime.Import.SetResult(runtime.Module);
        Assert.Empty(await search);
        Assert.DoesNotContain("search", runtime.Module.Calls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
        await picker.SetCustomEmojisAsync([]);
        Assert.DoesNotContain("setCustom", runtime.Module.Calls);
    }

    [Fact]
    public async Task ColorPickerDestroyFailure_StillReleasesModuleAndCallback()
    {
        var runtime = new DeferredRuntime();
        runtime.Import.SetResult(runtime.Module);
        runtime.Module.FailDestroy = true;
        var picker = new TestColorPicker(runtime);
        await picker.RenderAsync();
        await picker.DisposeAsync();
        Assert.Equal(1, runtime.Module.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() => runtime.Module.ColorCallback!.Value);
    }

    [Fact]
    public async Task InputClosedDuringImport_ReleasesLateModuleWithoutCreatingContext()
    {
        var runtime = new DeferredRuntime();
        var input = new TestInput(runtime);
        var render = input.RenderAsync();
        await ((IAsyncDisposable)input).DisposeAsync();
        runtime.Import.SetResult(runtime.Module);
        await render;
        Assert.DoesNotContain("init", runtime.Module.Calls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
    }

    [Fact]
    public async Task BrowserUtilsClosedDuringImport_DoesNotInstallListenersOrSignalReady()
    {
        var runtime = new DeferredRuntime();
        var component = new TestBrowserUtils(runtime);
        var ready = false;
        component.OnReady = () => { ready = true; return Task.CompletedTask; };
        var render = component.RenderAsync();
        await component.DisposeAsync();
        runtime.Import.SetResult(runtime.Module);
        await render;
        Assert.False(ready);
        Assert.DoesNotContain("init", runtime.Module.Calls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
    }

    private sealed class TestBrowserUtils : BrowserUtils
    {
        public TestBrowserUtils(IJSRuntime runtime)
        {
            Inject(this, "JsRuntime", runtime);
            OnInitialized();
        }
        public Task RenderAsync() => OnAfterRenderAsync(true);
    }

    [Fact]
    public async Task MouseListenerClosedDuringImport_DoesNotStartListeners()
    {
        var runtime = new DeferredRuntime();
        var component = new TestMouseListener(runtime);
        var render = component.RenderAsync();
        await component.DisposeAsync();
        runtime.Import.SetResult(runtime.Module);
        await render;
        Assert.DoesNotContain("init", runtime.Module.Calls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
    }

    [Fact]
    public async Task TargetScannerClosedDuringImport_DoesNotInstallDragListener()
    {
        var runtime = new DeferredRuntime();
        var component = new TestTargetScanner(runtime);
        var render = component.RenderAsync();
        await component.DisposeAsync();
        runtime.Import.SetResult(runtime.Module);
        await render;
        Assert.DoesNotContain("init", runtime.Module.Calls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
        Assert.Null(Valour.Client.Components.DockWindows.WindowTargetScanner.JsService);
    }

    private sealed class TestMouseListener : MouseListener
    {
        public TestMouseListener(IJSRuntime runtime) => Inject(this, "JsRuntime", runtime);
        public Task RenderAsync() => OnAfterRenderAsync(true);
    }

    private sealed class TestTargetScanner : Valour.Client.Components.DockWindows.WindowTargetScanner
    {
        public TestTargetScanner(IJSRuntime runtime) => Inject(this, "JsRuntime", runtime);
        public Task RenderAsync() => OnAfterRenderAsync(true);
    }

    [Fact]
    public async Task VoiceResetFailure_StillReleasesTheModule()
    {
        var module = new TestModule { FailReset = true };
        var component = new Valour.Client.Components.Calls.RealtimeKitComponent();
        typeof(Valour.Client.Components.Calls.RealtimeKitComponent)
            .GetField("_jsModule", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, module);
        await component.DisposeAsync();
        Assert.Equal(1, module.DisposeCalls);
    }

    [Fact]
    public async Task ClosedWindow_RemovesItsDragHandlers()
    {
        var component = new Valour.Client.Components.DockWindows.WindowComponent();
        var method = component.GetType().GetMethod("OnDragTab", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handler = method.CreateDelegate<Func<Valour.Client.Utility.MousePosition, Task>>(component);
        MouseListener.SubscribeMouseMove(handler);
        await ((IAsyncDisposable)component).DisposeAsync();
        var eventField = typeof(MouseListener).GetField("OnMouseMove", BindingFlags.Static | BindingFlags.NonPublic)!;
        var remaining = (MulticastDelegate?)eventField.GetValue(null);
        Assert.DoesNotContain(handler, remaining?.GetInvocationList() ?? []);
    }

    private sealed class TestInput : Valour.Client.Components.Windows.ChannelWindows.InputComponent
    {
        public TestInput(IJSRuntime runtime) => Inject(this, "JsRuntime", runtime);
        public Task RenderAsync() => OnAfterRenderAsync(true);
    }

    private static void Inject(object target, string name, object value)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is null) continue;
            property.SetValue(target, value);
            return;
        }
        throw new InvalidOperationException(name);
    }

    private sealed class TestFade : Fade
    {
        public TestFade(IJSRuntime runtime) => Inject(this, "JsRuntime", runtime);
        public Task RenderAsync() => OnAfterRenderAsync(true);
    }

    private sealed class TestColorPicker : ColorPickerComponent
    {
        public TestColorPicker(IJSRuntime runtime) => Inject(this, "JsRuntime", runtime);
        public Task RenderAsync() => OnAfterRenderAsync(true);
    }

    private sealed class DeferredRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TestModule Module = new();
        public async ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => (T)(object)await Import.Task;
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    private sealed class TestModule : IJSObjectReference
    {
        public List<string> Calls = [];
        public int DisposeCalls;
        public bool HoldInit;
        public bool FailDestroy;
        public bool FailReset;
        public bool CallbackAliveAtDestroy;
        public DotNetObjectReference<ColorPickerComponent>? ColorCallback;
        public TaskCompletionSource InitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InitCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "reset" && FailReset) throw new JSException("JS object no longer exists");
            if (identifier == "init")
            {
                ColorCallback = args![1] as DotNetObjectReference<ColorPickerComponent>;
                InitStarted.TrySetResult();
                if (HoldInit) await InitCompleted.Task;
            }
            if (identifier == "destroy")
            {
                CallbackAliveAtDestroy = ColorCallback?.Value is not null;
                if (FailDestroy) throw new JSException("WebView was removed");
            }
            return default!;
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<T>(identifier, args);
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }
}
