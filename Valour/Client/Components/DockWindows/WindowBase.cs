using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.DockWindows;

public abstract class WindowBase : ComponentBase
{
    public abstract Task ChangeType(Type newType, object newData, string newTitle, string newIcon);
    public abstract Task CloseAsync();
    public abstract void NotifyNeedsReRender();
    public abstract Task AddSiblingWindow(WindowData newTabData);
}