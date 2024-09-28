namespace Valour.Client.Components.DockWindows;

public enum SplitDirection
{
    None,
    Horizontal,
    Vertical
}

public class WindowSplit
{
    /// <summary>
    /// Unique identifier for the split
    /// </summary>
    public string Id { get; private set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// The layout this split belongs to
    /// </summary>
    public WindowLayout Layout { get; private set; }
    
    /// <summary>
    /// The state of the split. None if not split, Horizontal if split horizontally, Vertical if split vertically
    /// </summary>
    public SplitDirection SplitDirection { get; set; }
    
    /// <summary>
    /// The ratio of the split. 0.5 is 50/50, 0.25 is 25/75, etc.
    /// </summary>
    public float SplitRatio { get; set; }
    
    public WindowSplitComponent Component { get; set; }
    
    public WindowSplit(WindowLayout layout)
    {
        SplitDirection = SplitDirection.Horizontal;
        SplitRatio = 0.5f;
        
        Layout = layout;
    }
    
    public WindowSplit(WindowLayout layout, SplitDirection direction, float ratio)
    {
        SplitDirection = direction;
        SplitRatio = ratio;
        
        Layout = layout;
    }
}