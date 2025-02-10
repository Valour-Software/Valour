using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Sidebar;

public abstract class TabStripItem
{
    public string Name;
    public string Icon;
    public int Size;
    
    public TabStripItem(string name, string icon, int size)
    {
        Name = name;
        Icon = icon;
        Size = size;
    }
    
    public virtual RenderFragment RenderContent => builder =>
    {
    };
}

public class TabStripItem<T> : TabStripItem where T : ComponentBase 
{
    public TabStripItem(string name, string icon, int size)
        : base(name, icon, size)
    {
        Name = name;
        Icon = icon;
        Size = size;
    }
    
    public override RenderFragment RenderContent => builder =>
    {
        builder.OpenComponent<T>(0);
        builder.CloseComponent();
    };
}