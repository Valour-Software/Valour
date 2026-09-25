using Npgsql;
using Valour.Config.Configs;
using Valour.Server.Services;

namespace Valour.Server.Workers;

/// <summary>
/// Seals plain-text history written before end-to-end encryption and removes
/// expired encryption records.
///
/// Sealing is off unless <see cref="E2eeConfig.SealLegacyMessages"/> is set.
/// It starts once the server has finished starting and
/// <see cref="E2eeConfig.LegacySealStartDelaySeconds"/> have passed, so an
/// instance that is about to replace another one does not take over planets
/// early. Only the server holding a PostgreSQL advisory lock seals, and it
/// runs batches back to back until no plain-text messages remain.
/// </summary>
public class E2eeMaintenanceWorker : BackgroundService
{
    /// <summary>Advisory lock key held by the one server that seals history.</summary>
    public const long SealingLockKey = 0x56_45_32_45_53_45_41_4C; // "VE2ESEAL"

    private static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BusyInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    private readonly ILogger<E2eeMaintenanceWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;

    // A dedicated, unpooled connection that holds the sealing lock for as
    // long as this server seals. Closing it releases the lock.
    private NpgsqlConnection _lockConnection;

    public E2eeMaintenanceWorker(ILogger<E2eeMaintenanceWorker> logger, IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await WaitForStartAsync(stoppingToken))
            return;

        var nextCleanup = DateTime.UtcNow;
        var sealFrom = DateTime.UtcNow +
                       TimeSpan.FromSeconds(Math.Max(0, E2eeConfig.Current.LegacySealStartDelaySeconds));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = IdleInterval;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var maintenance = scope.ServiceProvider.GetRequiredService<E2eeMaintenanceService>();

                    if (E2eeConfig.Current.SealLegacyMessages && DateTime.UtcNow >= sealFrom &&
                        await HoldSealingLockAsync(stoppingToken))
                    {
                        var batchSize = Math.Clamp(E2eeConfig.Current.LegacySealBatchSize, 10, 5000);
                        var result = await maintenance.SealLegacyBatchAsync(batchSize);
                        if (result.DidWork)
                        {
                            _logger.LogInformation(
                                "Sealed {Sealed} plain-text messages from before encryption and removed the text of {Cleared} in deleted channels",
                                result.Sealed, result.Cleared);
                            delay = BusyInterval;
                        }
                    }
                    else
                    {
                        await ReleaseSealingLockAsync();
                    }

                    if (DateTime.UtcNow >= nextCleanup)
                    {
                        await maintenance.CleanupAsync();
                        nextCleanup = DateTime.UtcNow + CleanupInterval;
                    }
                }
                catch (Exception ex)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, "Error during end-to-end encryption maintenance");
                }

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            await ReleaseSealingLockAsync();
        }
    }

    /// <summary>Waits until the server has finished starting. Returns false if it stops first.</summary>
    private async Task<bool> WaitForStartAsync(CancellationToken stoppingToken)
    {
        if (_lifetime.ApplicationStarted.IsCancellationRequested)
            return true;

        var started = new TaskCompletionSource();
        await using var onStarted = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await using var onStopping = stoppingToken.Register(() => started.TrySetCanceled());
        try
        {
            await started.Task;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes or confirms the sealing lock. Another server holding it means
    /// this one does not seal.
    /// </summary>
    private async Task<bool> HoldSealingLockAsync(CancellationToken stoppingToken)
    {
        if (_lockConnection is not null)
        {
            try
            {
                await using var check = new NpgsqlCommand("SELECT 1", _lockConnection);
                await check.ExecuteScalarAsync(stoppingToken);
                return true;
            }
            catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
            {
                _logger.LogWarning(e, "Lost the connection that holds the legacy sealing lock");
                await ReleaseSealingLockAsync();
            }
        }

        // The connection sits idle between batches. Keepalives stop a firewall
        // or load balancer from silently dropping it, which would leave the
        // lock held by a session the database still thinks is alive, and let
        // the server notice a dead peer and release the lock.
        var builder = new NpgsqlConnectionStringBuilder(ValourDb.ConnectionString)
        {
            Pooling = false,
            KeepAlive = 30,
            TcpKeepAlive = true,
            TcpKeepAliveTime = 60,
            TcpKeepAliveInterval = 10
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(stoppingToken);
            await using var take = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            take.Parameters.AddWithValue("key", SealingLockKey);
            if (await take.ExecuteScalarAsync(stoppingToken) is true)
            {
                _lockConnection = connection;
                _logger.LogInformation("This server now seals plain-text history from before encryption");
                return true;
            }
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        {
            _logger.LogWarning(e, "Could not take the legacy sealing lock");
        }

        await connection.DisposeAsync();
        return false;
    }

    private async Task ReleaseSealingLockAsync()
    {
        if (_lockConnection is null)
            return;

        var connection = _lockConnection;
        _lockConnection = null;
        try
        {
            // Closing the unpooled connection ends its session, which also
            // releases the lock; unlocking first makes the release immediate.
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            release.Parameters.AddWithValue("key", SealingLockKey);
            await release.ExecuteScalarAsync();
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        {
            // The session is gone, so the lock is already released.
        }

        await connection.DisposeAsync();
    }
}
