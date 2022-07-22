namespace Valour.Client.Windows;

public interface IWindowHolder
{
    public abstract Task<bool> OpenWindow(ClientWindow window);
    public abstract Task CloseWindow(ClientWindow window);
    public abstract Task ReplaceWindow(ClientWindow old, ClientWindow newWindow);
}

