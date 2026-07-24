using System.Threading.Channels;
using ThreadChannel = System.Threading.Channels.Channel;

namespace Valour.Server.Workers;

/// <summary>
/// A queued request to evaluate channel activity for notification fan-out.
/// Produced on the message hot path (Redis counters only); consumed here so
/// candidate resolution and delivery never block message posting.
/// See Docs/ChannelActivityNotifications.md.
/// </summary>
public class ChannelActivityEvaluation
{
    public long ChannelId;
    public long PlanetId;
    public long TriggerMessageId;
    public int WindowMessageCount;
    public int WindowAuthorCount;
    public long[] WindowAuthorUserIds = [];
    public bool ConversationStart;
}

public class ChannelActivityWorker : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<ChannelActivityWorker> _logger;

    private readonly Channel<ChannelActivityEvaluation> _evalChannel =
        ThreadChannel.CreateUnbounded<ChannelActivityEvaluation>();

    private Task? _executingTask;
    private CancellationTokenSource? _cts;

    public ChannelActivityWorker(
        ILogger<ChannelActivityWorker> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public ValueTask QueueEvaluation(ChannelActivityEvaluation evaluation)
        => _evalChannel.Writer.WriteAsync(evaluation);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executingTask = Task.Run(() => ProcessQueueAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task ProcessQueueAsync(CancellationToken token)
    {
        try
        {
            while (await _evalChannel.Reader.WaitToReadAsync(token))
            {
                while (_evalChannel.Reader.TryRead(out var evaluation))
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var activityService = scope.ServiceProvider.GetRequiredService<ChannelActivityService>();
                        await activityService.EvaluateAsync(evaluation);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error evaluating channel activity for channel {ChannelId}",
                            evaluation.ChannelId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the token is canceled.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in channel activity queue processing.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Channel Activity Worker is stopping");
        if (_cts != null)
        {
            await _cts.CancelAsync();
        }

        if (_executingTask != null)
        {
            await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
