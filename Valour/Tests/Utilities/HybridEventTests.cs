using Valour.Shared.Utilities;

namespace Valour.Tests.Utilities;

public class HybridEventTests
{
    [Fact]
    public async Task Invoke_RunsSyncAndAsyncHandlers_UntilRemoved()
    {
        var hybrid = new HybridEvent<int>();
        var sync = 0;
        var asyncTotal = 0;
        Action<int> syncHandler = x => sync += x;
        Func<int, Task> asyncHandler = async x =>
        {
            await Task.Delay(1);
            Interlocked.Add(ref asyncTotal, x);
        };

        hybrid += syncHandler;
        hybrid += asyncHandler;
        hybrid.Invoke(2);
        hybrid.Invoke(3);

        hybrid -= syncHandler;
        hybrid.Invoke(5);

        Assert.Equal(5, sync);
        await WaitFor(() => Volatile.Read(ref asyncTotal) == 10);
    }

    [Fact]
    public void Invoke_ThrowingHandlerDoesNotStopOthers()
    {
        var hybrid = new HybridEvent<int>();
        var reached = false;
        hybrid += (Action<int>)(_ => throw new InvalidOperationException());
        hybrid += (Action<int>)(_ => reached = true);

        hybrid.Invoke(1);

        Assert.True(reached);
    }

    [Fact]
    public void Invoke_HandlerRemovedDuringInvocation_DoesNotCorruptRegistration()
    {
        var hybrid = new HybridEvent<int>();
        var calls = 0;
        Action<int>? self = null;
        self = _ =>
        {
            calls++;
            hybrid -= self!;
        };
        hybrid += self;

        hybrid.Invoke(1);
        hybrid.Invoke(1);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Dispose_ClearsHandlers()
    {
        var hybrid = new HybridEvent<int>();
        var calls = 0;
        hybrid += (Action<int>)(_ => calls++);

        hybrid.Dispose();
        hybrid.Invoke(1);

        Assert.Equal(0, calls);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);

        Assert.True(condition());
    }
}
