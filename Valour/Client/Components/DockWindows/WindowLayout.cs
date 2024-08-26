namespace Valour.Client.Components.DockWindows;

public enum SplitDirection
{
    None,
    Horizontal,
    Vertical
}

/// <summary>
/// Window Layout Positions are used to store the position of a WindowLayout relative to its parent.
/// </summary>
public struct WindowLayoutPosition
{
    // Relative to parent in percentage //
    public float Width { get; set; }
    public float Height { get; set; }
    
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    
    // Absolute position modifier in pixels
    public int WidthPixelModifier { get; set; }
    public int HeightPixelModifier { get; set; }
    
    public int OffsetXPixelModifier { get; set; }
    public int OffsetYPixelModifier { get; set; }

    public string Style =>
        @$"width: calc({Width}% + {WidthPixelModifier}px); height: calc({Height}% + {HeightPixelModifier}px); left: calc({OffsetX}% + {OffsetXPixelModifier}px); top: calc({OffsetY}% + {OffsetYPixelModifier}px);";
}

public class WindowSplit
{
    /// <summary>
    /// Unique identifier for the split
    /// </summary>
    public string Id { get; private set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// The state of the split. None if not split, Horizontal if split horizontally, Vertical if split vertically
    /// </summary>
    public SplitDirection SplitDirection { get; set; }
    
    /// <summary>
    /// The ratio of the split. 0.5 is 50/50, 0.25 is 25/75, etc.
    /// </summary>
    public float SplitRatio { get; set; }
    
    public WindowSplit()
    {
        SplitDirection = SplitDirection.Horizontal;
        SplitRatio = 0.5f;
    }
    
    public WindowSplit(SplitDirection direction, float ratio)
    {
        SplitDirection = direction;
        SplitRatio = ratio;
    }
}

public class WindowLayout
{
    /// <summary>
    /// Size of the gutter/slider between two windows
    /// </summary>
    const int SliderSize = 6;
    
    /// <summary>
    /// The dock component this layout is attached to
    /// </summary>
    public WindowDockComponent DockComponent { get; set; }
    
    /// <summary>
    /// The parent (if any) layout of this WindowLayout
    /// </summary>
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
    
    public WindowLayout(WindowDockComponent dockComponent, WindowLayout parent = null, List<WindowTab> tabs = null)
    {
        DockComponent = dockComponent;
        Parent = parent;

        Tabs = tabs ?? new List<WindowTab>();
        
        // Calculate initial position
        RecalculatePosition();
    }
    
    // A note on bool render:
    // This is used to prevent multiple layout changes from rendering the layout multiple times.
    // For example, one operation may both add and remove a tab. We don't want to render the layout twice.

    public Task AddTab(WindowContent content, bool render = true)
    {
        var tab = new WindowTab(content);
        return AddTab(tab, render);
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
            ChildOne.AddTab(tab, false);
            return;
        }
        
        // Add tab to list
        Tabs.Add(tab);
        
        // Set the layout of the tab
        tab.SetLayout(this, false);
        
        // Set the active tab if there is none
        SetFocusedTab(tab, false);

        // Notify base dock that a change has occurred
        if (render)
        {
            DockComponent.NotifyLayoutChanged();
        }
        
        // Let the tab know it has been opened
        await tab.NotifyOpened();
    }
    
    public async Task SetFocusedTab(WindowTab tab, bool render = true)
    {
        // If the tab is not in the layout, return
        if (!Tabs.Contains(tab))
            return;
        
        // Set the focused tab
        FocusedTab = tab;
        
        // Notify base dock that a change has occurred
        DockComponent.NotifyLayoutChanged();
        
        // Let the tab know it has been focused
        await tab.NotifyFocused();
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

    public void RemoveTab(WindowTab tab, bool render = true)
    {
        if (!Tabs.Contains(tab))
            return;
        
        Tabs.Remove(tab);
        tab.SetLayout(null, false);

        if (render)
        {
            DockComponent.NotifyLayoutChanged();
        }
    }

    public Task OnTabDropped(WindowTab tab, WindowDropTargets.DropLocation location)
    {
        if (location == WindowDropTargets.DropLocation.Center)
        {
            return AddTab(tab);
        }

        // If we are split, we cannot split further. This event should have been handled by the child.
        if (IsSplit)
        {
            Console.WriteLine("Tried to split a split layout. This should not happen.");
            return Task.CompletedTask;
        }

        return AddSplit(tab, location);
    }

    public async Task AddSplit(WindowTab startingTab, WindowDropTargets.DropLocation location)
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
        Split = new WindowSplit(direction, 0.5f);

        List<WindowTab> tabsOne;
        List<WindowTab> tabsTwo;
        
        if (location == WindowDropTargets.DropLocation.Left || location == WindowDropTargets.DropLocation.Up)
        {
            tabsOne = new List<WindowTab>();
            tabsTwo = Tabs;
            
            // Create children
            ChildOne = new WindowLayout(DockComponent, this, tabsOne);
            ChildTwo = new WindowLayout(DockComponent, this, tabsTwo);
            
            // Add the starting tab
            await ChildOne.AddTab(startingTab, false);
        }
        else
        {
            tabsOne = Tabs;
            tabsTwo = new List<WindowTab>();
            
            // Create children
            ChildOne = new WindowLayout(DockComponent, this, tabsOne);
            ChildTwo = new WindowLayout(DockComponent, this, tabsTwo);
            
            // Add the starting tab
            await ChildTwo.AddTab(startingTab, false);
        }
        
        // Re-render layouts
        DockComponent.NotifyLayoutChanged();
    }

    public void RecalculatePosition()
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
                
                _position.WidthPixelModifier = -(SliderSize / 2);
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
                
                _position.WidthPixelModifier = -(SliderSize / 2);
                _position.HeightPixelModifier = 0;
                _position.OffsetXPixelModifier = SliderSize;
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
                _position.HeightPixelModifier = -(SliderSize / 2);
                _position.OffsetXPixelModifier = 0;
                _position.OffsetYPixelModifier = 0;
            }
            else
            {
                _position.Width = Parent.Position.Width;
                _position.Height = (Parent.Position.Height * (1 - Parent.Split.SplitRatio));
                _position.OffsetX = Parent.Position.OffsetX;
                _position.OffsetY = Parent.Position.OffsetY + (Parent.Position.Height * Parent.Split.SplitRatio) + SliderSize;
                
                _position.WidthPixelModifier = 0;
                _position.HeightPixelModifier = -(SliderSize / 2);
                _position.OffsetXPixelModifier = 0;
                _position.OffsetYPixelModifier = SliderSize;
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
    }
}