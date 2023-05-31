using System.Collections.Concurrent;

namespace Valour.Server.Workers.Economy;

/// <summary>
/// The transaction worker processes incoming economic transactions
/// </summary>
public class TransactionWorker : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionWorker> _logger;

    /// <summary>
    /// The queue of transactions to process
    /// </summary>
    private static readonly BlockingCollection<Transaction> TransactionQueue
         = new BlockingCollection<Transaction>();

    /// <summary>
    /// Holds the long-running queue task
    /// </summary>
    private static Task _queueTask;

    // Timer for executing timed tasks
    private Timer _timer;

    public TransactionWorker(ILogger<TransactionWorker> logger,
                             IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Adds a transaction to the queue
    /// </summary>
    public static void AddToQueue(Transaction transaction)
        => TransactionQueue.Add(transaction);

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Transaction Worker");

        // Start the queue task
        _queueTask = Task.Run(async () => await ConsumeTransactionQueue(stoppingToken));

        _timer = new Timer(DoTimedWork, stoppingToken, TimeSpan.Zero,
                TimeSpan.FromSeconds(30));

        return Task.CompletedTask;
    }

    /// <summary>
    /// This task should run forever and consume transactions from
    /// the queue.
    /// </summary>
    private async Task ConsumeTransactionQueue(CancellationToken stoppingToken)
    {
        foreach (var transaction in TransactionQueue.GetConsumingEnumerable(stoppingToken))
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var hubService = scope.ServiceProvider.GetRequiredService<CoreHubService>();
            var ecoService = scope.ServiceProvider.GetRequiredService<EcoService>();
            
            var result = await ecoService.ProcessTransactionAsync(transaction, hubService);
            if (!result.Success)
                _logger.LogWarning("Transaction on queue failed: {Result}", result.Message);
        }
    }

    /// <summary>
    /// Performs the work to be done on a set schedule
    /// </summary>
    private void DoTimedWork(object state)
    {
        CancellationToken stoppingToken = (CancellationToken)state;

        // First check if queue task is running
        if (_queueTask.IsCompleted && !stoppingToken.IsCancellationRequested)
        {
            // If not, restart it
            _queueTask = Task.Run(async () => await ConsumeTransactionQueue(stoppingToken));

            _logger.LogInformation($@"Eco Transaction Worker queue task stopped at: {DateTime.UtcNow}
                                                 Restarting queue task.");
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transaction Worker is Stopping.");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
