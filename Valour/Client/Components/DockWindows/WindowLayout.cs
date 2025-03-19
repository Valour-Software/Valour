using System.Text.Json.Serialization;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Client.Components.Windows.HomeWindows;
using Valour.Sdk.Models;

namespace Valour.Client.Components.DockWindows;

/// <summary>
/// Window Layout Positions are used to store the position of a WindowLayout relative to its parent.
/// </summary>
public class WindowLayoutPosition
{
    public static readonly WindowLayoutPosition FullScreen = new WindowLayoutPosition
    {
        Width = 100,
        Height = 100,
        OffsetX = 0,
        OffsetY = 0,
        WidthPixelModifier = 0,
        HeightPixelModifier = 0,
        OffsetXPixelModifier = 0,
        OffsetYPixelModifier = 0
    };
    
    // Relative to parent in percentage //
    public float Width { get; set; }
    public float Height { get; set; }
    
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    
    // Absolute position modifier in pixels
    public float WidthPixelModifier { get; set; }
    public float HeightPixelModifier { get; set; }
    
    public float OffsetXPixelModifier { get; set; }
    public float OffsetYPixelModifier { get; set; }

    public string Style =>
        @$"width: calc({Width}% + {WidthPixelModifier}px); height: calc({Height}% + {HeightPixelModifier}px); left: calc({OffsetX}% + {OffsetXPixelModifier}px); top: calc({OffsetY}% + {OffsetYPixelModifier}px);";
}

public class WindowLayout
{
    /// <summary>
    /// Size of the gutter/slider between two windows
    /// </summary>
    public static readonly float SliderSize = 6;
    
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;
    /// <summary>
    /// The dock component this layout is attached to
    /// </summary>
    public WindowDockComponent DockComponent { get; set; }
    
    /// <summary>
    /// The parent (if any) layout of this WindowLayout
    /// </summary>
    [JsonIgnore]
    public WindowLayout Parent { get; private set; }
    
    /// <summary>
    /// The split of this WindowLayout. Null if not split.
    /// </summary>
    public WindowSplit Split { get; set; }
    
    /// <summary>
    /// The first child of the WindowLayout if split. Either left or top.
    /// </summary>
    public WindowLayout ChildOne { get; private set; }
    
    /// <summary>
    /// The second child of the WindowLayout if split. Either right or bottom.
    /// </summary>
    public WindowLayout ChildTwo { get; private set; }
    
    /// <summary>
    /// The tabs in the WindowLayout, if not split.
    /// </summary>
    public List<WindowTab> Tabs { get; set; }
    
    /// <summary>
    /// The active tab in the WindowLayout
    /// </summary>
    public WindowTab FocusedTab { get; private set; }
    
    /// <summary>
    /// If the WindowLayout is split
    /// </summary>
    public bool IsSplit => Split is not null;
    
    private WindowLayoutPosition _position;
    public WindowLayoutPosition Position => _position;
    
    public WindowLayout(WindowDockComponent dockComponent, WindowLayout parent = null)
    {
        DockComponent = dockComponent;
        Parent = parent;

        Tabs = new List<WindowTab>();
        
        _position = new WindowLayoutPosition();
        
        // Calculate initial position
        RecalculatePosition();
    }
    
    // For loading from serialized
    public WindowLayout(WindowLayout childOne, WindowLayout childTwo, WindowSplit split, List<WindowTab> tabs, int focusedTabIndex = 0)
    {
        ChildOne = childOne;
        ChildTwo = childTwo;
        
        Split = split;
        
        Tabs = tabs;

        if (Tabs is not null && Tabs.Count > 0)
        {
            if (focusedTabIndex >= tabs.Count)
                focusedTabIndex = 0;
            else
                FocusedTab = Tabs[focusedTabIndex];
        }

        // Set layout of everything to this
        childOne?.SetParentRaw(this);
        childTwo?.SetParentRaw(this);
        
        split?.SetLayoutRaw(this);

        if (tabs is not null)
        {
            foreach (var tab in tabs)
            {
                tab.SetLayoutRaw(this);
            }
        }
        
        _position = new WindowLayoutPosition();
    }
    
    // A note on bool render:
    // This is used to prevent multiple layout changes from rendering the layout multiple times.
    // For example, one operation may both add and remove a tab. We don't want to render the layout twice.

    public void SetTabsRaw(List<WindowTab> tabs)
    {
        Tabs = tabs;

        foreach (var tab in tabs)
        {
            if (tab.Layout != this)
            {
                tab.SetLayoutRaw(this);
            }
        }
    }
    
    public void SetDockRecursiveRaw(WindowDockComponent dockComponent)
    {
        DockComponent = dockComponent;
        
        if (ChildOne is not null)
        {
            ChildOne.SetDockRecursiveRaw(dockComponent);
        }
        
        if (ChildTwo is not null)
        {
            ChildTwo.SetDockRecursiveRaw(dockComponent);
        }
    }
    
    public void SetParentRaw(WindowLayout parent)
    {
        Parent = parent;
    }
    
    public async Task AddTabs(List<WindowTab> tabs, bool render = true)
    {
        foreach (var tab in tabs)
        {
            await AddTab(tab, false);
        }

        if (render)
        {
            DockComponent.NotifyLayoutChanged();
        }
    }
    
    public async Task<WindowTab> AddTab(WindowContent content, bool render = true)
    {
        var tab = new WindowTab(content);
        await AddTab(tab, render);
        return tab;
    }

    public async Task AddTab(WindowTab tab, bool render = true)
    {
        // Make sure we don't already contain the tab
        if (Tabs.Contains(tab))
            return;
        
        // If this is split, add the tab to the first child.
        // We cannot add tabs to a split layout.
        if (IsSplit)
        {
            await ChildOne.AddTab(tab, false);
            return;
        }
        
        // Add tab to list
        Tabs.Add(tab);
        
        // Set the layout of the tab
        await tab.SetLayout(this, false);
        
        // Set the active tab if there is none
        await SetFocusedTab(tab);

        // Notify base dock that a change has occurred
        if (render)
        {
            DockComponent.NotifyLayoutChanged();
        }
        
        // Notify tabs of tab-stack change
        NotifyTabsOfChange();
        
        // Let the tab know it has been opened
        await tab.NotifyOpened();
    }
    
    public async Task SetFocusedTab(WindowTab tab)
    {
        // If the tab is not in the layout, return
        if (!Tabs.Contains(tab))
            return;
        
        // If the tab is already focused, return
        if (FocusedTab == tab)
            return;
        
        // Set the focused tab
        FocusedTab = tab;
        
        // Let the tab know it has been focused
        await tab.NotifyFocused();

        // Notify tabs of tab-stack change
        NotifyTabsOfChange();
        
        // Set global focused tab
        await WindowService.SetFocusedTab(tab);
    }

    public void NotifyTabsOfChange()
    {
        foreach (var tab in Tabs)
        {
            tab.NotifyLayoutChanged();
        }
    }
    
    /// <summary>
    /// Adds all contained tabs to the given list
    /// </summary>
    public void GetTabs(List<WindowTab> tabs)
    {
        // If this is split, get the tabs from both children
        if (IsSplit)
        {
            ChildOne.GetTabs(tabs);
            ChildTwo.GetTabs(tabs);
            return;
        }
        
        // Add tabs to list
        tabs.AddRange(Tabs);
    }
    
    public void GetSplits(List<WindowSplit> splits)
    {
        // If this is split, add the split to the list
        if (IsSplit)
        {
            // Get splits from children
            ChildOne.GetSplits(splits);
            ChildTwo.GetSplits(splits);
            
            splits.Add(Split);
        }
    }

    public async Task RemoveTab(WindowTab tab, bool render = true)
    {
        if (!Tabs.Contains(tab))
            return;
        
        Tabs.Remove(tab);
        await tab.SetLayout(null, false);
        
        if (Tabs.Count == 0)
        {
            // If we're out of tabs, this layout should be removed
            if (Parent is null)
            {
                // If this is the root layout, we can't remove it
                // so instead we add the home tab
                await AddTab(HomeWindowComponent.DefaultContent);
            }
            else
            {
                Parent?.RemoveChild(this);
            }
        }

        if (render)
        {
            DockComponent.NotifyLayoutChanged();
        }
        
        // Notify tabs of tab-stack change
        NotifyTabsOfChange();
    }

    public async Task OnTabDropped(WindowTab tab, WindowDropTargets.DropLocation location)
    {
        // Remove tab as floater
        await DockComponent.RemoveFloatingTab(tab);
        
        if (location == WindowDropTargets.DropLocation.Center)
        {
            await AddTab(tab);
        }

        // If we are split, we cannot split further. This event should have been handled by the child.
        if (IsSplit)
        {
            Console.WriteLine("Tried to split a split layout. This should not happen.");
        }

        AddSplit(tab, location);
    }
    
    public async Task OnChannelDropped(Channel channel, WindowDropTargets.DropLocation location)
    {
        // Create a new chat window
        var chatWindow = await ChatWindowComponent.GetDefaultContent(channel);
        var tab = new WindowTab(chatWindow);
        
        if (location == WindowDropTargets.DropLocation.Center)
        {
            await AddTab(tab);
        }

        // If we are split, we cannot split further. This event should have been handled by the child.
        if (IsSplit)
        {
            Console.WriteLine("Tried to split a split layout. This should not happen.");
        }

        AddSplit(tab, location);
    }

    private void AddSplit(WindowTab startingTab, WindowDropTargets.DropLocation location)
    {
        // If we are already split, we cannot split further
        if (IsSplit)
            return;        
        
        // Ensure location is valid
        if (location == WindowDropTargets.DropLocation.Center)
            return;
        
        var direction = 
            (location == WindowDropTargets.DropLocation.Left || location == WindowDropTargets.DropLocation.Right)
                ? SplitDirection.Horizontal
                : SplitDirection.Vertical;
        
        // Create split
        Split = new WindowSplit(this, direction, 0.5f);

        List<WindowTab> tabsOne;
        List<WindowTab> tabsTwo;
        
        if (location == WindowDropTargets.DropLocation.Left || location == WindowDropTargets.DropLocation.Up)
        {
            tabsOne = new List<WindowTab>()
            {
                startingTab
            };
            tabsTwo = Tabs;
            
            // Create children
            ChildOne = new WindowLayout(DockComponent, this);
            ChildTwo = new WindowLayout(DockComponent, this);
            
            ChildOne.SetTabsRaw(tabsOne);
            ChildTwo.SetTabsRaw(tabsTwo);
        }
        else
        {
            tabsOne = Tabs;
            tabsTwo = new List<WindowTab>()
            {
                startingTab
            };
            
            // Create children
            ChildOne = new WindowLayout(DockComponent, this);
            ChildTwo = new WindowLayout(DockComponent, this);

            ChildOne.SetTabsRaw(tabsOne);
            ChildTwo.SetTabsRaw(tabsTwo);
        }
        
        // Clear tabs
        Tabs = null;
        
        RecalculatePosition();
        
        // Re-render layouts
        DockComponent.NotifyLayoutChanged();
    }

    public void RemoveChild(WindowLayout child)
    {
        // Both children get removed but the other one becomes the tab stack
        if (ChildOne == child)
        {
            // Take tabs from child
            Tabs = ChildTwo.Tabs;
            ChildTwo.Parent = null;
            ChildTwo = null;
            
            // Remove other child
            ChildOne = null;
        }
        else if (ChildTwo == child)
        {
            // Take tabs from child
            Tabs = ChildOne.Tabs;
            ChildOne.Parent = null;
            ChildOne = null;
            
            // Remove other child
            ChildTwo = null;
        }
        else
        {
            Console.WriteLine("Tried to remove a child that is not a child of this layout.");
            return;
        }
        
        // Remove split
        Split = null;
        
        RecalculatePosition();
        
        // Re-render tabs 
        foreach (var tab in Tabs)
        {
            // Set tab layouts to this layout
            tab.SetLayoutRaw(this);
            tab.NotifyLayoutChanged();
        }
        
        DockComponent.NotifyLayoutChanged();
    }

    public float GetwidthPx()
    {
        if (Parent is null)
        {
            return DockComponent.Dimensions.Width;
        }
        
        if (Parent.Split.SplitDirection == SplitDirection.Vertical)
        {
            return Parent.GetwidthPx();
        }
        
        var multiplier = Parent.ChildOne == this ? Parent.Split.SplitRatio : 1 - Parent.Split.SplitRatio;
        return Parent.GetwidthPx() * multiplier;
    }
    
    public float GetHeightPx()
    {
        if (Parent is null)
        {
            return DockComponent.Dimensions.Height;
        }
        
        if (Parent.Split.SplitDirection == SplitDirection.Horizontal)
        {
            return Parent.GetHeightPx();
        }
        
        var multiplier = Parent.ChildOne == this ? Parent.Split.SplitRatio : 1 - Parent.Split.SplitRatio;
        return Parent.GetHeightPx() * multiplier;
    }

    public void RecalculatePosition()
    {
        if (Parent is null)
        {
            _position = WindowLayoutPosition.FullScreen;
        }
        else
        {
            // If the parent is not split, the position is the same as the parent
            // Technically this shouldn't happen, but we'll cover the edge case
            if (!Parent.IsSplit)
            {
                _position = Parent.Position;
            }

            // If the parent is split horizontally
            if (Parent.Split.SplitDirection == SplitDirection.Horizontal)
            {
                // If this is the first child
                if (Parent.ChildOne == this)
                {
                    _position.Width = Parent.Position.Width * Parent.Split.SplitRatio;
                    _position.Height = Parent.Position.Height;
                    _position.OffsetX = Parent.Position.OffsetX;
                    _position.OffsetY = Parent.Position.OffsetY;

                    _position.WidthPixelModifier = 0;
                    _position.HeightPixelModifier = 0;
                    _position.OffsetXPixelModifier = 0;
                    _position.OffsetYPixelModifier = 0;
                }
                // If this is the second child
                else
                {
                    _position.Width = (Parent.Position.Width * (1 - Parent.Split.SplitRatio));
                    _position.Height = Parent.Position.Height;
                    _position.OffsetX = Parent.Position.OffsetX + (Parent.Position.Width * Parent.Split.SplitRatio);
                    _position.OffsetY = Parent.Position.OffsetY;

                    _position.WidthPixelModifier = 0;
                    _position.HeightPixelModifier = 0;
                    _position.OffsetXPixelModifier = 0;
                    _position.OffsetYPixelModifier = 0;
                }
            }
            else
            {
                if (Parent.ChildOne == this)
                {
                    _position.Width = Parent.Position.Width;
                    _position.Height = (Parent.Position.Height * Parent.Split.SplitRatio);
                    _position.OffsetX = Parent.Position.OffsetX;
                    _position.OffsetY = Parent.Position.OffsetY;

                    _position.WidthPixelModifier = 0;
                    _position.HeightPixelModifier = 0;
                    _position.OffsetXPixelModifier = 0;
                    _position.OffsetYPixelModifier = 0;
                }
                else
                {
                    _position.Width = Parent.Position.Width;
                    _position.Height = (Parent.Position.Height * (1 - Parent.Split.SplitRatio));
                    _position.OffsetX = Parent.Position.OffsetX;
                    _position.OffsetY = Parent.Position.OffsetY + (Parent.Position.Height * Parent.Split.SplitRatio);

                    _position.WidthPixelModifier = 0;
                    _position.HeightPixelModifier = 0;
                    _position.OffsetXPixelModifier = 0;
                    _position.OffsetYPixelModifier = 0;
                }
            }
        }

        // Recalculate children
        if (ChildOne is not null)
        {
            ChildOne.RecalculatePosition();
        }
        
        if (ChildTwo is not null)
        {
            ChildTwo.RecalculatePosition();
        }
        
        // Console.WriteLine("Position: " + JsonSerializer.Serialize(_position));
    }
    
    public void ReRenderRecursive()
    {
        // Re-render tabs 
        if (Tabs is not null)
        {
            foreach (var tab in Tabs)
            {
                tab.Component?.ReRender();
            }
        }

        // Re-render children
        if (ChildOne is not null)
        {
            ChildOne.ReRenderRecursive();
        }
        
        if (ChildTwo is not null)
        {
            ChildTwo.ReRenderRecursive();
        }
    }
}