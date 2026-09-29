namespace Valour.Shared.Models;

/// <summary>
/// The start of a GitHub gist, stored in a GitHub attachment's Data so
/// clients can show the code without loading GitHub's embed script.
/// </summary>
public class GistPreview
{
    public string Owner { get; set; }
    public string Description { get; set; }

    /// <summary>
    /// Number of files in the gist; Files holds only the first few.
    /// </summary>
    public int FileCount { get; set; }

    public List<GistPreviewFile> Files { get; set; } = new();
}

public class GistPreviewFile
{
    public string Name { get; set; }
    public string Language { get; set; }

    /// <summary>
    /// The first lines of the file, each cut to a bounded length.
    /// </summary>
    public List<string> Lines { get; set; } = new();

    public int TotalLines { get; set; }
}
