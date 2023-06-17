using System.Collections.Specialized;
using FastDragBlazor.Components;
using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Client.Windows;
using Valour.Client.Windows.ChatWindows;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Client.Components.ChannelList;

public class NestedChannel
{
    public PlanetChannel Channel { get; set; }
    public bool IsCategory { get; set; }
    public int Depth { get; set; }
    public string IconUrl { get; set; }
    public string AltText { get; set; }
    public bool IsUnread { get; set; }
    public NestedChannel Parent { get; set; }
    public List<NestedChannel> Children { get; set; }
    public Planet Planet { get; set; }
    public PlanetRole DefaultRole { get; set; }
    private ChannelsComponent _listComponent;

    public DragItem<NestedChannel> Component;
    

    public NestedChannel(ChannelsComponent listComponent, Planet planet, PlanetRole defaultRole, PlanetChannel channel)
    {
        Planet = planet;
        DefaultRole = defaultRole;
        Channel = channel;
        IsCategory = true;
        _listComponent = listComponent;

        ((PlanetCategory)channel).OnUpdated += OnChannelUpdate;
    }
    
    public NestedChannel(NestedChannel parent, PlanetChannel channel, int depth = 0)
    {
        Parent = parent;
        Planet = Parent.Planet;
        Depth = depth;
        Channel = channel;
        IsCategory = channel is PlanetCategory;
        DefaultRole = Parent.DefaultRole;
        _listComponent = Parent._listComponent;
        
        switch (Channel)
        {
            case PlanetCategory category:
                category.OnUpdated += OnChannelUpdate;
                break;
            case PlanetChatChannel chatChannel:
                chatChannel.OnUpdated += OnChannelUpdate;
                break;
            case PlanetVoiceChannel voiceChannel:
                voiceChannel.OnUpdated += OnChannelUpdate;
                break;
        }
    }
    
    public async Task OnChannelUpdate(ModelUpdateEvent eventData)
    {
        if (eventData.PropsChanged.Contains(nameof(Channel.Name)))
        {
            Component.Refresh();
        }
    }

    public string GetIconStyle()
    {
        if (!IsCategory)
            return string.Empty;
        
        return Component.Expanded ? "transform: rotate(90deg);" : string.Empty;
    }

    public async Task OnClicked()
    {
        switch (Channel)
        {
            case PlanetCategory:
            {
                _listComponent.ToggleExpand(Component);
                break;
            }
            case PlanetChatChannel chatChannel:
            {
                var window = WindowManager.Instance.GetSelectedWindow();
                if (window is ChatChannelWindow currentChatWindow)
                {
                    // It's the same channel, cancel
                    if (currentChatWindow.Channel.Id == Channel.Id)
                    {
                        return;
                    }

                    Console.WriteLine(Channel.Name);
                    await currentChatWindow.Component.SwapChannel(chatChannel);
                }
                break;
            }
        }
    }

    public async Task LoadChildren(List<PlanetChannel> allChannels)
    {
        Children = new List<NestedChannel>();
        
        // Categories load their children
        if (IsCategory)
        {
            foreach (var channel in allChannels)
            {
                if (channel.ParentId == Channel.Id)
                    Children.Add(new NestedChannel(this, channel, Depth + 1));
            }
            
            Children.Sort((a, b) => a.Channel.Position.CompareTo(b.Channel.Position));
            
            foreach (var child in Children)
            {
                await child.LoadChildren(allChannels);
            }
        }

        // We load ourself after our children because it's easier to know if we are unread
        await LoadSelf();
    }
    
    public async Task LoadSelf()
    {
        switch (Channel)
        {
            case PlanetCategory:
            {
                IsUnread = false;
                foreach (var child in Children)
                {
                    if (child.IsUnread)
                    {
                        IsUnread = true;
                        break;
                    }
                }
                
                if (IsUnread)
                {
                    IconUrl = "_content/Valour.Client/media/Category-Icon-unread.svg";
                    AltText = "Category with unread messages";
                }
                else
                {
                    IconUrl = "_content/Valour.Client/media/Category-Icon-read.svg";
                    AltText = "Category with no unread messages";
                }

                break;
            }
            case PlanetChatChannel channel:
            {
                IsUnread = ValourClient.GetChannelUnreadState(channel.Id);
                var hasLock = false;

                var node = await Channel.GetPermNodeAsync(DefaultRole.Id, PermChannelType.PlanetChatChannel);
                if (node is not null) {
                    var state = node.GetPermissionState(ChatChannelPermissions.View);
                    if (state == PermissionState.False)
                        hasLock = true;
                }

                if (IsUnread)
                {
                    if (hasLock)
                    {
                        IconUrl = "_content/Valour.Client/media/Channel-Filled-Icon-with-lock.svg";
                        AltText = "Private chat channel with unread messages";
                    }
                    else
                    {
                        IconUrl = "_content/Valour.Client/media/Channel-Filled-Icon.svg";
                        AltText = "Public chat channel with unread messages";
                    }
                }
                else
                {
                    if (hasLock)
                    {
                        IconUrl = "_content/Valour.Client/media/Channel-Icon-with-lock.svg";
                        AltText = "Private chat channel with no unread messages";
                    }
                    else
                    {
                        IconUrl = "_content/Valour.Client/media/Channel-Icon.svg";
                        AltText = "Public chat channel with no unread messages";
                    }
                }
                    
                break;
            }
            case PlanetVoiceChannel:
            {
                IconUrl = "_content/Valour.Client/media/voice-channel-icon.svg";
                AltText = "Public voice channel";
                break;
            }
                
        }
    }
    
}