using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Valour.Client.Components.Users;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class UserBadgeRenderingTests
{
    [Theory]
    [InlineData(22113735421460480, PlatformBadge.None, true, false)]
    [InlineData(22113735421460480, PlatformBadge.FirstOneThousand, false, false)]
    [InlineData(22113735421460481, PlatformBadge.None, false, true)]
    [InlineData(22113735421460481, PlatformBadge.FirstTenThousand, false, false)]
    [InlineData(42076534464053249, PlatformBadge.None, false, false)]
    public async Task MilestoneVisibility_DoesNotDisplayAnUnearnedReplacement(
        long id, PlatformBadge hidden, bool showOneThousand, bool showTenThousand)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var user = new User(null) { Id = id, HiddenBadgeFlags = (long)hidden, ValourStaff = true };

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<UserBadges>(ParameterView.FromDictionary(
                new Dictionary<string, object> { [nameof(UserBadges.User)] = user }));
            var html = output.ToHtmlString();
            Assert.Equal(showOneThousand, html.Contains("milestone-1k"));
            Assert.Equal(showTenThousand, html.Contains("milestone-10k"));
            Assert.Contains("Valour Staff", html);
        });
    }
}
