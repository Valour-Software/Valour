using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.DockWindows;

public abstract class WindowBase : ComponentBase
{
    public abstract Task ReplaceAsync(WindowContent content);
    public abstract Task CloseAsync();
    public abstract void NotifyNeedsReRender();
    public abstract Task AddSiblingWindow(WindowTab newTabTab);
}