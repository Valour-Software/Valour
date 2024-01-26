using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Valour.Server.API;

public static class RateLimitDefs
{
    public static void AddRateLimitDefs(IServiceCollection services)
    {
        services.AddRateLimiter(_ =>
        {
            _.AddFixedWindowLimiter("login", options =>
            {
                options.PermitLimit = 5;
                options.Window = TimeSpan.FromSeconds(60);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 2;
            });
        });
    }
}