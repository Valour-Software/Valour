using Microsoft.AspNetCore.Components;

namespace Valour.Client.ContextMenu;

public class ContextMenu<T> : ComponentBase
{
    [Parameter]
    public T Data { get; set; }

    public async Task CloseAsync()
    {
        await ContextMenuService.Root.CloseMenu();
    }
}