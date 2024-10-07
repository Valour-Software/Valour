using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Utility.DragList;

public abstract class DragListItem
{
    /// <summary>
    /// The drag list this item belongs to
    /// </summary>
    public DragListComponent DragList { get; set; }
    
    /// <summary>
    /// The id of the parent item
    /// </summary>
    public abstract Task<DragListItem> GetDragParent();

    /// <summary>
    /// The children of this 
    /// </summary>
    public abstract Task<List<DragListItem>> GetDragChildren();
    
    public int RenderedDepth { get; set; }

    public async Task AddToListRecursive(DragListComponent component, List<DragListItem> list, int depth = 0)
    {
        DragList = component;
        RenderedDepth = depth;
        list.Add(this);

        var children = await GetDragChildren();
        
        if (children is null)
            return;
        
        foreach (var child in children)
        {
            await child.AddToListRecursive(component, list, depth + 1);
        }
    }
    
    /// <summary>
    /// The position of this item relative to other items
    /// at the same depth (think nested objects)
    /// </summary>
    public int RelativePosition { get; set; }
    
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
}