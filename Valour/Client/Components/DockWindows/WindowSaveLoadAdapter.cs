using System.Text.Json;
using Valour.Sdk.Client;

namespace Valour.Client.Components.DockWindows;

public class WindowSplitState
{
    public SplitDirection SplitDirection { get; set; }
    public float SplitRatio { get; set; }
}

public class WindowContentState
{
    public string Title { get; set; }
    public string Icon { get; set; }
    public long? PlanetId { get; set; }
    public bool AutoScroll { get; set; }
    
    public string ComponentType { get; set; }
    
    // Will be null if it's a WindowContent or WindowContent<TWindow>
    // Will not be null if it's a WindowContent<TWindow, TData>
    public string DataJson { get; set; }
}

public class WindowTabState
{
    public string ContentType { get; set; }
    public WindowContentState ContentState { get; set; }
    public FloatingWindowProps FloatingWindowProps { get; set; }
}

public class WindowLayoutState
{
    // Children
    public WindowLayoutState ChildOne { get; set; }
    public WindowLayoutState ChildTwo { get; set; }
    
    // Split data
    public WindowSplitState SplitState { get; set; }
    
    public List<WindowTabState> TabStates { get; set; }
}

public class WindowSaveLoadAdapter
{
    private ValourClient _client;
    
    public WindowSaveLoadAdapter(ValourClient client)
    {
        _client = client;
    }
    
    public string SerializeLayout(WindowLayout layout)
    {
        var state = Export(layout);
        var json = JsonSerializer.Serialize(state);
        return json;
    }
    
    public async Task<WindowLayout> DeserializeLayout(string json)
    {
        var state = JsonSerializer.Deserialize<WindowLayoutState>(json);
        var layout = await Import(state);
        return layout;
    }

    public string SerializeFloaters(List<WindowTab> floaters)
    {
        if (floaters is null)
        {
            return null;
        }
        
        var tabStates = Export(floaters);
        var json = JsonSerializer.Serialize(tabStates);
        return json;
    }
    
    public async Task<List<WindowTab>> DeserializeFloaters(string json)
    {
        var tabStates = JsonSerializer.Deserialize<List<WindowTabState>>(json);
        var tabs = new List<WindowTab>();
        
        foreach (var tabState in tabStates)
        {
            var tab = await Import(tabState);
            tabs.Add(tab);
        }
        
        return tabs;
    }

    public List<WindowTabState> Export(List<WindowTab> tabs)
    {
        List<WindowTabState> tabStates = null;

        if (tabs is not null)
        {
            tabStates = new List<WindowTabState>();
            
            foreach (var tab in tabs)
            {
                string data = null;
                
                // Use reflection to check if the content has an ExportData method
                var method = tab.Content.GetType().GetMethod("ExportData");
                if (method is not null)
                {
                    data = (string)method.Invoke(tab.Content, [_client]);
                }

                var tabState = new WindowTabState
                {
                    ContentType = tab.Content.GetType().FullName,
                    ContentState = new WindowContentState
                    {
                        Title = tab.Content.Title,
                        Icon = tab.Content.Icon,
                        PlanetId = tab.Content.PlanetId,
                        AutoScroll = tab.Content.AutoScroll,
                        ComponentType = tab.Content.ComponentType.FullName,
                        DataJson = data
                    },
                    FloatingWindowProps = tab.FloatingProps
                };
                
                tabStates.Add(tabState);
            }
        }
        
        return tabStates;
    }
    
    public WindowLayoutState Export(WindowLayout layout)
    {
        if (layout is null)
        {
            return null;
        }

        var tabStates = Export(layout.Tabs);
        
        WindowSplitState splitState = null;
        
        if (layout.Split is not null)
        {
            splitState = new WindowSplitState
            {
                SplitDirection = layout.Split.SplitDirection,
                SplitRatio = layout.Split.SplitRatio
            };
        }
        
        var state = new WindowLayoutState
        {
            ChildOne = Export(layout.ChildOne),
            ChildTwo = Export(layout.ChildTwo),
            SplitState = splitState,
            TabStates = tabStates
        };
        
        return state;
    }
    
    public async Task<WindowLayout> Import(WindowLayoutState state)
    {
        if (state is null)
        {
            return null;
        }

        WindowLayout childOne = null;
        WindowLayout childTwo = null;
        
        List<WindowTab> tabs = null;

        if (state.ChildOne is not null && state.ChildTwo is not null)
        {
            childOne = await Import(state.ChildOne);
            childTwo = await Import(state.ChildTwo);
        }
        else
        {
            tabs = new List<WindowTab>();
            
            foreach (var tab in state.TabStates)
            {
                tabs.Add(await Import(tab));
            }
        }
        
        WindowSplit split = null;
        
        if (state.SplitState is not null)
        {
            split = new WindowSplit(null)
            {
                SplitDirection = state.SplitState.SplitDirection,
                SplitRatio = state.SplitState.SplitRatio
            };
        }
        
        var layout = new WindowLayout(childOne, childTwo, split, tabs);
        
        return layout;
    }
    
    public async Task<WindowTab> Import(WindowTabState state)
    {
        if (state is null)
        {
            return null;
        }
        
        // Use reflection to rebuild window content
        var contentType = Type.GetType(state.ContentType);
        
        if (contentType is null)
        {   
            Console.WriteLine($"!!! Could not find content type: {state.ContentType} when loading window layout tab state");
            return null;
        }
        
        var content = (WindowContent)Activator.CreateInstance(contentType);
        
        if (content is null)
        {
            Console.WriteLine($"!!! Could not create instance of component type: {state.ContentState.ComponentType} when loading window layout tab state");
            return null;
        }
        
        content.Title = state.ContentState.Title;
        content.Icon = state.ContentState.Icon;
        content.PlanetId = state.ContentState.PlanetId;
        content.AutoScroll = state.ContentState.AutoScroll;

        if (state.ContentState.DataJson is not null)
        {
            // Use reflection to set the data property
            var method = content.GetType().GetMethod("ImportData");
            if (method is not null)
            {
                
                // Invoke as async method
                var task = (Task)method.Invoke(content, [state.ContentState.DataJson, _client]);
                if (task is not null)
                {
                    await task;
                }
                else
                {
                    Console.WriteLine($"!!! Failed to convert import to task on component type: {state.ContentState.ComponentType} when loading window layout tab state");
                }
            }
            else
            {
                Console.WriteLine($"!!! Could not find import data method on component type: {state.ContentState.ComponentType} when loading window layout tab state");
            }
        }
        
        var tab = new WindowTab(content, state.FloatingWindowProps);
        
        return tab;
    }
}