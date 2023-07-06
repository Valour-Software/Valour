using Microsoft.AspNetCore.Components;
using Valour.Api.Models;

namespace Valour.Client.Components.Sidebar.ChannelList;

public abstract class ChannelListItemComponent : ComponentBase
{
    [CascadingParameter]
    public PlanetListComponent PlanetComponent { get; set; }
    
    [Parameter]
    public CategoryListComponent ParentCategory { get; set; }
    
    public abstract PlanetChannel GetItem();
    
    private bool _render;
    
    public void Refresh()
    {
        _render = true;
        StateHasChanged();
    }
    
    protected override bool ShouldRender() => _render;

    protected override void OnAfterRender(bool firstRender)
    {
        _render = false;
    }
}