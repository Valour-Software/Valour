namespace Valour.Client.Components.DockWindows;

public class WindowData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; }
    public string Icon { get; set; }
    public Type Type { get; set; }
    public object Data { get; set; }
    public DockWindow DockWindow { get; set; }
}