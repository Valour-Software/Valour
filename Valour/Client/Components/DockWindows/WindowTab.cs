using Microsoft.AspNetCore.Components;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Client.Components.Windows.HomeWindows;
using Valour.Sdk.Client;
using Valour.Sdk.Extensions;
using Valour.Sdk.Models;

namespace Valour.Client.Components.DockWindows;

public abstract class WindowContent
{
    /// <summary>
    /// Event that is called when the window content is focused (ie clicked on)
    /// </summary>
    public event Func<Task> OnFocused;
    
    /// <summary>
    /// Event that is called when the window content is closed
    /// </summary>
    public event Func<Task> OnClosed;
    
    /// <summary>
    /// Event that is called when the window content is opened
    /// </summary>
    public event Func<Task> OnOpened;
    
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
    
    public async Task NotifyFocused()
    {
        if (OnFocused is not null)
            await OnFocused.Invoke();
    }
    
    public async Task NotifyClosed()
    {
        if (OnClosed is not null)
            await OnClosed.Invoke();
        
        if (PlanetId is not null)
        {
            var planet = await Planet.FindAsync(PlanetId.Value);
            await ValourClient.ClosePlanetConnection(planet, Id);
        }
    }
    
    public async Task NotifyOpened()
    {
        if (OnOpened is not null)
            await OnOpened.Invoke();

        if (PlanetId is not null)
        {
            var planet = await Planet.FindAsync(PlanetId.Value);
            await ValourClient.OpenPlanetConnection(planet, Id);
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
        builder.AddComponentParameter(1, "WindowCtx", this);
        builder.CloseComponent();
    };
}

public class WindowContent<TWindow, TData> : 
    WindowContent<TWindow> where TWindow : WindowContentComponent<TData> where TData : class
{
    public TData Data { get; set; }
    
    public override TData ComponentData => Data;
    
    public override RenderFragment RenderContent => builder =>
    {
        builder.OpenComponent<TWindow>(0);
        builder.AddComponentParameter(1, "WindowCtx", this);
        builder.AddComponentParameter(2, "Data", Data);
        builder.CloseComponent();
    };
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
    /// <summary>
    /// Event that is called when the window tab is focused (ie clicked on)
    /// </summary>
    public event Func<Task> OnFocused;
    
    /// <summary>
    /// Event that is called when the window tab is closed
    /// </summary>
    public event Func<Task> OnClosed;
    
    /// <summary>
    /// Event that is called when the window tab is opened
    /// </summary>
    public event Func<Task> OnOpened;
    
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
    public WindowLayout Layout { get; private set; }
    
    /// <summary>
    /// If the window is floating
    /// </summary>
    public bool IsFloating => FloatingProps is not null;
    
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
        
        if (oldContent is not null)
        {
            // Let old content know it's closing
            await oldContent.NotifyClosed();
        }
        
        // Render new component. We do this first because it feels
        // much faster to the user
        Component?.ReRender();
        
        // Let new content know it's opening
        await content.NotifyOpened();
    }

    public void NotifyLayoutChanged()
    {
        Component?.NotifyLayoutChanged();
    }
    
    /// <summary>
    /// Sets the layout for this window tab to render within
    /// </summary>
    public void SetLayout(WindowLayout layout, bool render = true)
    {
        // If the layout is the same, return
        if (Layout == layout)
            return;
        
        // Get reference to old layout
        var oldLayout = Layout;
        
        // Set layout
        Layout = layout;
        
        // Remove from old layout
        oldLayout?.RemoveTab(this, false);
        
        // Add to new layout
        Layout?.AddTab(this, render);
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
            WindowService.TryAddFloatingWindow(tab);
        }
    }

    public void SetFloatingProps(FloatingWindowProps props)
    {
        FloatingProps = props;
    }

    public async Task NotifyAdded()
    {
        if (Content?.PlanetId is not null)
        {
            var planet = await Planet.FindAsync(Content.PlanetId.Value);
            await ValourClient.OpenPlanetConnection(planet, Id);
        }
    }
    
    public async Task NotifyFocused()
    {
        if (WindowService.FocusedTab == this)
            return;
        
        if (OnFocused is not null)
            await OnFocused.Invoke();
        
        // Call for content
        await Content.NotifyFocused();
        
        // Set global focused tab
        await WindowService.SetFocusedTab(this);
    }
    
    public async Task NotifyClose()
    {
        if (OnClosed is not null)
            await OnClosed.Invoke();

        // Call for content
        await Content.NotifyClosed();
    }
    
    public async Task NotifyOpened()
    {
        if (OnOpened is not null)
            await OnOpened.Invoke();
            
        // Call for content
        await Content.NotifyOpened();
    }
}