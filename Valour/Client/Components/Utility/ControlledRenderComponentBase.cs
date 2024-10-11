using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Utility;

public abstract class ControlledRenderComponentBase : ComponentBase
{
    private bool _canRender = true;
    
    protected override bool ShouldRender()
    {
        return _canRender;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        _canRender = false;
    }
    
    public void ReRender()
    {
        _canRender = true;
        StateHasChanged();
    }
}