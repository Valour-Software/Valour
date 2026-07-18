namespace Valour.Client.Components.Wiki.Display;

/// <summary>
/// Plain display data for one docs tree node. Consumed by WikiTreeDisplay in
/// both the interactive app and the public server-rendered docs pages, so it
/// must stay free of SDK types.
/// </summary>
public class WikiTreeNodeData
{
    public long Id { get; set; }
    public string Title { get; set; }

    /// <summary>
    /// Link target for pages. On public pages this is the public docs URL;
    /// in-app usage intercepts clicks instead.
    /// </summary>
    public string Href { get; set; }

    public bool IsFolder { get; set; }
    public bool IsActive { get; set; }
    public bool IsExpanded { get; set; } = true;
    public List<WikiTreeNodeData> Children { get; set; } = new();
}
