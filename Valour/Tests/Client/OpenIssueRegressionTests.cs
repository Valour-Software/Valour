using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Valour.Client.Components.DockWindows;
using Valour.Client.Components.Users;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Client.Components.Windows.PlanetInfo;
using Valour.Client.Utility;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class OpenIssueRegressionTests
{
    [Theory]
    [InlineData("de-DE", 3)]
    [InlineData("fr-FR", 6)]
    [InlineData("en-US", 7)]
    public void TabStyles_UseCssDecimalPoints(string culture, int count)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var layout = new WindowLayout(null);
            for (var i = 0; i < count; i++)
            {
                var tab = new WindowTab(new PlanetInfoWindowComponent.Content());
                tab.SetLayoutRaw(layout);
                layout.Tabs.Add(tab);
            }

            var component = new WindowComponent { WindowTab = layout.Tabs[1] };
            var style = (string)typeof(WindowComponent).GetProperty("TabWrapperStyle",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            Assert.Equal(FormattableString.Invariant(
                $"width: calc({100f / count}% - {30f / count}px + 0px); margin-left: min(250px, calc({100f / count}% - {30f / count}px))"), style);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(42, null, true)]
    [InlineData(43, null, false)]
    [InlineData(42, "community.test", false)]
    public void PlanetInfoTabs_ComparePlanetAndOrigin(long id, string? domain, bool expected)
    {
        var existing = PlanetInfoWindowComponent.GetDefaultContent(new PlanetListInfo { PlanetId = 42 });
        var requested = PlanetInfoWindowComponent.GetDefaultContent(new PlanetListInfo { PlanetId = id, NodeDomain = domain });
        var method = typeof(WindowService).GetMethod("RepresentsSameContent", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, method.Invoke(null, [existing, requested]));
    }

    [Fact]
    public void DeletedReply_RemovesPreviewWithoutChangingResponse()
    {
        var client = new ValourClient("https://valour.test/");
        var source = new Message("deleted text", null, null, 1, 2, client) { Id = 42 };
        var response = new Message("keep response", null, null, 3, 2, client)
        {
            ReplyToId = source.Id, ReplyTo = source
        };
        Assert.False(ChatWindowComponent.ClearDeletedReply(response, 43));
        Assert.Same(source, response.ReplyTo);
        Assert.True(ChatWindowComponent.ClearDeletedReply(response, source.Id));
        Assert.Null(response.ReplyTo);
        Assert.Null(response.ReplyToId);
        Assert.Equal("keep response", response.Content);
        Assert.False(ChatWindowComponent.ClearDeletedReply(response, source.Id));
    }

    [Fact]
    public async Task MessageFromUnloadedPlanet_FallsBackWithoutThrowing()
    {
        // Staff review reported messages from planets they have not loaded.
        var client = new ValourClient("https://valour.test/");
        var message = new Message("reported", 47195723456315392, 5, 1, 2, client)
        {
            Id = 42,
            Mentions =
            [
                new Mention { Type = MentionType.PlanetMember, TargetId = 5 },
                new Mention { Type = MentionType.Role, TargetId = 6 }
            ]
        };

        Assert.Null(await message.FetchAuthorMemberAsync());
        Assert.False(message.CheckIfMentioned());
    }

#pragma warning disable BL0006
    [Fact]
    public void ProfileContextPress_UsesCustomEventArgumentsInRenderTree()
    {
        var component = new UserInfoComponent { User = new User(new ValourClient("https://valour.test/")) };
        using var builder = new RenderTreeBuilder();
        typeof(UserInfoComponent).GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, [builder]);
        var frames = builder.GetFrames();
        var handler = frames.Array.Take(frames.Count).Single(frame =>
            frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "oncontextpress");
        // The renderer infers the deserialization type from this delegate.
        Assert.IsType<Action<ContextPressEventArgs>>(handler.AttributeValue);
    }
#pragma warning restore BL0006
}
