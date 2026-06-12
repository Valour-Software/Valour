using Microsoft.AspNetCore.Components;
using Valour.Sdk.Models;

namespace Valour.Client.Utility;

public static class ChannelFragments
{
    public static RenderFragment<Channel> ChannelPill => channel => __builder =>
    {
        __builder.OpenComponent<Valour.Client.Components.UI.ChannelPill>(0);
        __builder.AddAttribute(1, "Channel", channel);
        __builder.CloseComponent();
    };
}
