namespace Valour.Client.Components.Windows.WikiWindows;

/// <summary>
/// Persisted state for a docs window. Edit drafts are intentionally never
/// persisted — restored windows always reopen in view mode.
/// </summary>
public class WikiWindowData
{
    public long PlanetId { get; set; }

    /// <summary>
    /// The selected doc (page or folder); null shows the docs home
    /// </summary>
    public long? SelectedPageId { get; set; }

    /// <summary>
    /// Folders the user has collapsed. Stored inverted so the default state
    /// (everything expanded, Docusaurus-style) needs no bookkeeping.
    /// </summary>
    public List<long> CollapsedFolderIds { get; set; } = new();
}
