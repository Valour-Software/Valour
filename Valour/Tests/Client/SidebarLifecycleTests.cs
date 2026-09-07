using System.Reflection;
using Microsoft.JSInterop;
using Valour.Client.Components.Sidebar;
using Valour.Client.Storage;
using Valour.Sdk.Client;

namespace Valour.Tests.Client;

public class SidebarLifecycleTests
{
    [Fact]
    public async Task ClosingDuringImport_DoesNotInitializeOrLeakLateModule()
    {
        var runtime = new DeferredRuntime();
        var sidebar = new TestSidebar(runtime);
        var render = sidebar.AfterRenderAsync();
        await runtime.ImportStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await sidebar.CloseAsync();
        runtime.ImportCompletion.SetResult(runtime.Module);
        await render.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, runtime.Module.InitCalls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
    }

    [Fact]
    public async Task ClosingDuringInit_CleansLateInstanceBeforeDisposingCallback()
    {
        var runtime = new DeferredRuntime();
        runtime.ImportCompletion.SetResult(runtime.Module);
        var sidebar = new TestSidebar(runtime);
        var render = sidebar.AfterRenderAsync();
        await runtime.Module.InitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await sidebar.CloseAsync();
        runtime.Module.InitCompletion.SetResult(runtime.Module.Instance);
        await render.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, runtime.Module.Instance.CleanupCalls);
        Assert.Equal(1, runtime.Module.Instance.DisposeCalls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
        Assert.True(runtime.Module.Instance.CallbackAliveDuringCleanup);
        Assert.Throws<ObjectDisposedException>(() => runtime.Module.Callback!.Value);
    }

    [Fact]
    public async Task ClosingInitializedSidebar_IsIdempotentAndReleasesEveryReference()
    {
        var runtime = new DeferredRuntime();
        runtime.ImportCompletion.SetResult(runtime.Module);
        runtime.Module.InitCompletion.SetResult(runtime.Module.Instance);
        var sidebar = new TestSidebar(runtime);
        await sidebar.AfterRenderAsync();

        await sidebar.CloseAsync();
        await sidebar.CloseAsync();
        await sidebar.AfterRenderAsync();

        Assert.Equal(1, runtime.Module.InitCalls);
        Assert.Equal(1, runtime.Module.Instance.CleanupCalls);
        Assert.Equal(1, runtime.Module.Instance.DisposeCalls);
        Assert.Equal(1, runtime.Module.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() => runtime.Module.Callback!.Value);
    }

    [Fact]
    public async Task LateStorageCompletion_DoesNotRegisterClosedSidebar()
    {
        var storage = new DeferredStorage();
        var sidebar = new TestSidebar(new DeferredRuntime(), storage);
        var initialize = sidebar.InitializeAsync();
        await storage.ContainsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await sidebar.CloseAsync();
        storage.ContainsCompletion.SetResult(false);
        await initialize.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(Sidebar._mainSidebar);
        var handlers = (MulticastDelegate?)typeof(Sidebar)
            .GetField("OnToggleMobileSidebar", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null);
        Assert.DoesNotContain(handlers?.GetInvocationList() ?? [], handler => ReferenceEquals(handler.Target, sidebar));
    }

    [Fact]
    public async Task ClosingPreviousSidebar_DoesNotClearItsReplacement()
    {
        var first = new TestSidebar(new DeferredRuntime(), DeferredStorage.Ready());
        var second = new TestSidebar(new DeferredRuntime(), DeferredStorage.Ready());
        try
        {
            await first.InitializeAsync();
            await second.InitializeAsync();
            await first.CloseAsync();
            Assert.Same(second, Sidebar._mainSidebar);
        }
        finally
        {
            await first.CloseAsync();
            await second.CloseAsync();
        }
        Assert.Null(Sidebar._mainSidebar);
    }

    [Fact]
    public async Task FailedInitialization_ReleasesReferencesAndPreservesTheError()
    {
        var runtime = new DeferredRuntime();
        runtime.ImportCompletion.SetResult(runtime.Module);
        runtime.Module.InitCompletion.SetException(new JSException("Deliberate init failure"));
        var sidebar = new TestSidebar(runtime);

        var error = await Assert.ThrowsAsync<JSException>(sidebar.AfterRenderAsync);

        Assert.Equal("Deliberate init failure", error.Message);
        Assert.Equal(1, runtime.Module.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() => runtime.Module.Callback!.Value);
        await sidebar.CloseAsync();
        Assert.Equal(1, runtime.Module.DisposeCalls);
    }

    private sealed class TestSidebar : Sidebar
    {
        public TestSidebar(IJSRuntime runtime, IAppStorage? storage = null)
        {
            var client = new ValourClient("http://localhost:5100/");
            Inject("JsRuntime", runtime);
            Inject("Client", client);
            Inject("PlanetService", client.PlanetService);
            Inject("LocalStorage", storage ?? DeferredStorage.Ready());
        }

        private void Inject(string name, object value) => typeof(Sidebar)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(this, value);

        public Task AfterRenderAsync() => base.OnAfterRenderAsync(true);
        public Task InitializeAsync() => base.OnInitializedAsync();
        public ValueTask CloseAsync() => ((IAsyncDisposable)this).DisposeAsync();
    }

    private sealed class DeferredRuntime : IJSRuntime
    {
        public readonly TaskCompletionSource ImportStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<IJSObjectReference> ImportCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly DeferredModule Module = new();

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("import", identifier);
            ImportStarted.TrySetResult();
            return (TValue)(object)await ImportCompletion.Task;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class DeferredModule : IJSObjectReference
    {
        public readonly TaskCompletionSource InitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<IJSObjectReference> InitCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly FakeInstance Instance = new();
        public DotNetObjectReference<Sidebar>? Callback;
        public int InitCalls;
        public int DisposeCalls;

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("init", identifier);
            InitCalls++;
            Callback = Assert.IsType<DotNetObjectReference<Sidebar>>(args![0]);
            _ = Callback.Value;
            Instance.Callback = Callback;
            InitStarted.TrySetResult();
            return (TValue)(object)await InitCompletion.Task;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class FakeInstance : IJSObjectReference
    {
        public DotNetObjectReference<Sidebar>? Callback;
        public int CleanupCalls;
        public int DisposeCalls;
        public bool CallbackAliveDuringCleanup;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("dispose", identifier);
            CleanupCalls++;
            CallbackAliveDuringCleanup = Callback!.Value is not null;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class DeferredStorage : IAppStorage
    {
        public readonly TaskCompletionSource ContainsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> ContainsCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static DeferredStorage Ready()
        {
            var storage = new DeferredStorage();
            storage.ContainsCompletion.SetResult(false);
            return storage;
        }

        public Task<bool> ContainsKeyAsync(string key) { ContainsStarted.TrySetResult(); return ContainsCompletion.Task; }
        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(null);
        public Task SetStringAsync(string key, string value) => Task.CompletedTask;
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
    }
}
