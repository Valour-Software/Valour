using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Valour.Client.Toast;
using Valour.Shared;

namespace Valour.Tests.Client;

public class SentryToastTests
{
    [Fact]
    public async Task FaultedTasks_ReturnFailedResultsAndRenderFailure()
    {
        using var services = Services();
        await using var renderer = new TestRenderer(services);
        var container = new ToastContainer();
        await renderer.Dispatcher.InvokeAsync(() => renderer.AttachAsync(container));
        var toast = new ProgressToastData<TaskResult> { ProgressTask = Task.FromException<TaskResult>(new IOException("offline")) };
        var result = await Task.Run(() => container.WaitToastWithTaskResult(toast));
        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
        Assert.Equal(ToastProgressState.Failure, toast.ProgressState);
        Assert.NotNull(toast.Card);
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task TypedTaskFailure_ReturnsFailureWithoutRethrowingAggregateException()
    {
        using var services = Services();
        await using var renderer = new TestRenderer(services);
        var container = new ToastContainer();
        await renderer.Dispatcher.InvokeAsync(() => renderer.AttachAsync(container));
        var toast = new ProgressToastData<TaskResult<string>> { ProgressTask = Task.FromException<TaskResult<string>>(new IOException("offline")), FailureMessage = "Please retry" };
        var result = await container.WaitToastWithTaskResult(toast);
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal("Please retry", toast.Message);
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task RemovingCardWhileTaskRuns_PreservesSuccessfulResult()
    {
        using var services = Services();
        await using var renderer = new TestRenderer(services);
        var container = new ToastContainer();
        await renderer.Dispatcher.InvokeAsync(() => renderer.AttachAsync(container));
        var completion = new TaskCompletionSource<TaskResult<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var toast = new ProgressToastData<TaskResult<int>> { ProgressTask = completion.Task };
        var pending = container.WaitToastWithTaskResult(toast);
        await renderer.Dispatcher.InvokeAsync(() => container.RemoveToast(toast));
        Assert.Null(toast.Card);
        completion.SetResult(TaskResult<int>.FromData(42));
        var result = await pending;
        Assert.True(result.Success);
        Assert.Equal(42, result.Data);
        Assert.Equal(ToastProgressState.Success, toast.ProgressState);
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task DisposingContainerWhileTaskRuns_DoesNotRenderOrLoseResult()
    {
        using var services = Services();
        await using var renderer = new TestRenderer(services);
        var container = new ToastContainer();
        await renderer.Dispatcher.InvokeAsync(() => renderer.AttachAsync(container));
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var toast = new ProgressToastData<int> { ProgressTask = completion.Task };
        var pending = container.WaitToastWithResult(toast);
        await renderer.DisposeAsync();
        completion.SetResult(42);
        Assert.Equal(42, await pending);
        Assert.Empty(container.ActiveToasts);
        Assert.Null(toast.Card);
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task CompletedTask_RendersSuccessAndAutomaticallyRemovesToast()
    {
        using var services = Services();
        await using var renderer = new TestRenderer(services);
        var container = new ToastContainer();
        await renderer.Dispatcher.InvokeAsync(() => renderer.AttachAsync(container));
        var toast = new ProgressToastData { ProgressTask = Task.CompletedTask, SuccessMessage = "Saved" };
        await container.WaitToast(toast);
        Assert.Equal(ToastProgressState.Success, toast.ProgressState);
        Assert.Equal("Saved", toast.Message);
        await Task.Delay(3000);
        await renderer.Dispatcher.InvokeAsync(() => Assert.Empty(container.ActiveToasts));
        Assert.Null(toast.Card);
        Assert.Empty(renderer.Errors);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(new Runtime()).BuildServiceProvider();

#pragma warning disable BL0006
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        public List<Exception> Errors { get; } = [];
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public Task AttachAsync(IComponent component) => RenderRootComponentAsync(AssignRootComponentId(component));
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => Errors.Add(exception);
    }
#pragma warning restore BL0006

    private sealed class Runtime : IJSRuntime, IJSObjectReference
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(identifier == "import" ? (T)(object)this : default!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<T>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
