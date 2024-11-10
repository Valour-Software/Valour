using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Utility.DragList;

public abstract class DragListItem
{
    /// <summary>
    /// The drag list this item belongs to
    /// </summary>
    public DragListComponent DragList { get; set; }
    
    /// <summary>
    /// True if this item can contain other items
    /// </summary>
    public bool Container { get; set; }
    
    /// <summary>
    /// True if this item is opened. Only applies to containers.
    /// </summary>
    public bool Open { get; set; }

    /// <summary>
    /// The amount of margin to apply to children. Only applies to containers.
    /// </summary>
    public int ChildMargin { get; set; }
    
    public abstract int Depth { get; }

    public abstract int Position { get; }
    
    public virtual Task OnClick()
    {
        if (Container)
        {
            // Switch the open state
            Open = !Open;
            
            // Re-render drag list
            DragList.ReRender();
        }

        return Task.CompletedTask;
    }
    
    /// <summary>
    /// The content to render for this item
    /// </summary>
    public RenderFragment Content { get; set; }
    
    public static int Compare(DragListItem a, DragListItem b)
    {
        // Compare position
        return a.Position.CompareTo(b.Position);
    }
}