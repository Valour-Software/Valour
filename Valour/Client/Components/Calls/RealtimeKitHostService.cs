namespace Valour.Client.Components.Calls;

public sealed class RealtimeKitHostService
{
    private readonly Lock _sync = new();
    private RealtimeKitComponent? _component;
    private TaskCompletionSource<RealtimeKitComponent> _componentReady = CreateReadyTcs();

    public event Action? AvailabilityChanged;

    public RealtimeKitComponent? Component
    {
        get
        {
            lock (_sync)
            {
                return _component;
            }
        }
    }

    public void Register(RealtimeKitComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        var changed = false;

        lock (_sync)
        {
            if (ReferenceEquals(_component, component))
                return;

            _component = component;
            _componentReady.TrySetResult(component);
            changed = true;
        }

        if (changed)
        {
            AvailabilityChanged?.Invoke();
        }
    }

    public void Unregister(RealtimeKitComponent component)
    {
        if (component is null)
            return;

        var changed = false;

        lock (_sync)
        {
            if (!ReferenceEquals(_component, component))
                return;

            _component = null;
            _componentReady = CreateReadyTcs();
            changed = true;
        }

        if (changed)
        {
            AvailabilityChanged?.Invoke();
        }
    }

    public async Task<RealtimeKitComponent?> WaitForComponentAsync(TimeSpan timeout)
    {
        RealtimeKitComponent? current;
        Task<RealtimeKitComponent> waitTask;

        lock (_sync)
        {
            current = _component;
            waitTask = _componentReady.Task;
        }

        if (current is not null)
            return current;

        try
        {
            return await waitTask.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static TaskCompletionSource<RealtimeKitComponent> CreateReadyTcs()
    {
        return new TaskCompletionSource<RealtimeKitComponent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
