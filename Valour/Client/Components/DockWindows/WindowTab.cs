using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Valour.Client.Device;
using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Client.Components.DockWindows;

public abstract class WindowContent
{
    public string Id { get; private set; } = Guid.NewGuid().ToString();
    public string Title { get; set; }
    public string Icon { get; set; }
    public long? PlanetId { get; set; }
    public bool AutoScroll { get; set; }
    
    public WindowTab Tab { get; set; }
    
    private WindowContentComponentBase _componentBase;
    
    public virtual Type ComponentType => _componentBase.GetType();

    public virtual object ComponentData => null;
    

    public virtual WindowContentComponentBase ComponentBase
    {
        get => _componentBase;
        private set {
            _componentBase = value;
        }
    }
    
    public void SetComponent(WindowContentComponentBase componentBase)
    {
        ComponentBase = componentBase;
    }
    
    public Task NotifyFocused()
    {
        return Task.CompletedTask;
    }
    
    public async Task NotifyClosed()
    {
        if (PlanetId is not null)
        {
            var planetService = Tab.Component.Dock.Client.PlanetService;
            await planetService.TryClosePlanetConnection(PlanetId.Value, Id);
        }
    }
    
    public async Task NotifyOpened()
    {
        if (PlanetId is not null)
        {
            var planetService = Tab.Layout.DockComponent.Client.PlanetService;
            await planetService.TryOpenPlanetConnection(PlanetId.Value, Id);
        }
    }
    
    public abstract RenderFragment RenderContent { get; }
}

public class WindowContent<TWindow> : WindowContent where TWindow : WindowContentComponentBase
{
    /// <summary>
    /// The component of the Window content -- not the Window Tab!
    /// </summary>
    public override TWindow ComponentBase
    {
        get => (TWindow) base.ComponentBase;
    }
    
    /// <summary>
    /// The type of the component for this window content
    /// </summary>
    public override Type ComponentType => typeof(TWindow);

    public override RenderFragment RenderContent => builder =>
    {
        builder.OpenComponent<TWindow>(0);
        builder.SetKey(Id);
        builder.AddComponentParameter(1, "WindowCtx", this);
        builder.CloseComponent();
    };
}

public abstract class WindowContent<TWindow, TData> : 
    WindowContent<TWindow> where TWindow : WindowContentComponent<TData> where TData : class
{
    public TData Data { get; set; }
    
    public override TData ComponentData => Data;
    
    public override RenderFragment RenderContent => builder =>
    {
        builder.OpenComponent<TWindow>(0);
        builder.SetKey(Id);
        builder.AddComponentParameter(1, "WindowCtx", this);
        builder.AddComponentParameter(2, "Data", Data);
        builder.CloseComponent();
    };

    /// <summary>
    /// Used to export data to a form which can be serialized
    /// </summary>
    public abstract string ExportData(ValourClient client);
    
    /// <summary>
    /// Used to import data from a serialized form to the data object
    /// </summary>
    public abstract Task ImportData(string data, ValourClient client);
}

public class FloatingWindowProps
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 400;
    public float Height { get; set; } = 400;    
}

public class WindowTab
{
    public event Func<Task> OnStartFloating;
    
    /// <summary>
    /// The unique identifier of the window tab
    /// </summary>
    public string Id { get; private set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// The history of the content the window tab has displayed
    /// </summary>
    public List<WindowContent> History { get; set; } = new();
    
    /// <summary>
    /// The current content of the window tab
    /// </summary>
    public WindowContent Content { get; private set; }
    
    /// <summary>
    /// The component for this window tab
    /// </summary>
    public WindowComponent Component { get; set; }
    
    /// <summary>
    /// The layout this window tab belongs to
    /// </summary>
    [JsonIgnore]
    public WindowLayout Layout { get; private set; }
    
    /// <summary>
    /// If the window is floating
    /// </summary>
    public bool IsFloating => Layout is null;
    
    /// <summary>
    /// The properties of the window if it is floating
    /// </summary>
    public FloatingWindowProps FloatingProps { get; private set; }
    
    public WindowTab(WindowContent content, FloatingWindowProps floatingProps = null)
    {
        FloatingProps = floatingProps;
        
        Content = content;
        content.Tab = this;
    }
    
    public async Task SetContent(WindowContent content)
    {
        var oldContent = Content;
        
        Content = content;
        content.Tab = this;
        
        // Render new component. We do this first because it feels
        // much faster to the user
        Component?.ReRender();
        
        if (oldContent is not null)
        {
            // Let old content know it's closing
            await oldContent.NotifyClosed();
        }
        
        // Let new content know it's opening
        await content.NotifyOpened();
        
        // Let dock know to save new state
        // Note to future self: this needs to run AFTER the new content
        // has loaded its properties that need to be saved
        await Layout.DockComponent.SaveLayout();
    }

    public void NotifyLayoutChanged()
    {
        Component?.NotifyLayoutChanged();
    }
    
    /// <summary>
    /// Sets the layout for this window tab to render within
    /// </summary>
    public async Task SetLayout(WindowLayout layout, bool render = true)
    {
        // If the layout is the same, return
        if (Layout == layout)
            return;
        
        // Get reference to old layout
        var oldLayout = Layout;
        
        // Set layout
        Layout = layout;
        
        // Remove from old layout
        if (oldLayout is not null)
            await oldLayout.RemoveTab(this, false);
        
        // Add to new layout
        if (Layout is not null) 
            await Layout.AddTab(this, render);
    }
    
    public void SetLayoutRaw(WindowLayout layout)
    {
        Layout = layout;
    }
    
    public async Task AddSiblingTab(WindowContent content)
    {
        var tab = new WindowTab(content);
        
        await AddSiblingTab(tab);
    }

    public async Task AddSiblingTab(WindowTab tab)
    {
        if (Layout is not null)
        {
            await Layout.AddTab(tab);
        }
        else
        {
            await WindowService.TryAddFloatingWindow(tab);
        }
    }

    public void SetFloatingProps(FloatingWindowProps props)
    {
        FloatingProps = props;
    }
    
    public async Task NotifyFocused()
    {
        // Call for content
        await Content.NotifyFocused();
    }
    
    public async Task NotifyClose()
    {
        // Call for content
        await Content.NotifyClosed();
    }
    
    public async Task NotifyOpened()
    {
        // Call for content
        await Content.NotifyOpened();
    }

    public async Task NotifyFloating()
    {
        if (OnStartFloating is not null)
            await OnStartFloating.Invoke();
    }
}