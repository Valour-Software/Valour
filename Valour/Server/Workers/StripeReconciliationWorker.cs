using Stripe.Checkout;
using Valour.Config.Configs;

namespace Valour.Server.Workers;

public class StripeReconciliationWorker : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StripeReconciliationWorker> _logger;

    private Timer _timer;
    private int _isRunning;

    public StripeReconciliationWorker(ILogger<StripeReconciliationWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(ReconcileSessions, null, TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30));

        return Task.CompletedTask;
    }

    private async void ReconcileSessions(object state)
    {
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
            return;

        try
        {
            if (StripeConfig.Current?.SecretKey is null)
                return;

            using var scope = _serviceProvider.CreateScope();
            var ecoService = scope.ServiceProvider.GetRequiredService<EcoService>();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var service = new SessionService();
            var options = new SessionListOptions
            {
                Status = "complete",
                Created = new Stripe.DateRangeOptions
                {
                    GreaterThanOrEqual = DateTime.UtcNow.AddHours(-2),
                },
                Limit = 100,
            };

            var sessions = await service.ListAsync(options);

            foreach (var session in sessions)
            {
                try
                {
                    // Metadata is always included in the response; it doesn't need expanding.
                    // Previously tried to expand "metadata" which caused a StripeException.
                    var fullSession = await service.GetAsync(session.Id);

                    if (fullSession.Mode == "subscription")
                        await Valour.Server.Api.Dynamic.StripeApi.FulfillSubscriptionSessionAsync(fullSession, ecoService, db, _logger);
                    else
                        await Valour.Server.Api.Dynamic.StripeApi.FulfillSessionAsync(fullSession, ecoService, db, _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reconciliation error for session {SessionId}", session.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe reconciliation worker error");
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stripe Reconciliation Worker is Stopping");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
