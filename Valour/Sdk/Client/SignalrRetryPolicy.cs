using Microsoft.AspNetCore.SignalR.Client;

namespace Valour.Sdk.Client;

public class SignalrRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
        => TimeSpan.FromSeconds(retryContext.PreviousRetryCount switch
            {
                0 => 1,
                1 => 2,
                2 => 4,
                3 => 8,
                4 => 16,
                5 => 32,
                >= 6 => 64,
                < 0 => throw new ArgumentOutOfRangeException(nameof(retryContext.PreviousRetryCount), retryContext.PreviousRetryCount, "Negative retry counts!"),
            });
}
