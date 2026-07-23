using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Sdk.Models.Embeds;

/// <summary>
/// An interactive embed attached to a message. Embeds contain one or more
/// pages, each holding a tree of items. Built with <see cref="EmbedBuilder"/>.
/// </summary>
public class Embed
{
    /// <summary>
    /// The current embed wire-format version. Payloads with any other
    /// version fail to parse and render nothing.
    /// </summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// The name of this embed. Should be set if the embed has forms.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The id of this embed. Should be set if the embed has forms.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Monotonic revision used to order live updates; clients drop updates
    /// with a lower revision than the one they are displaying.
    /// </summary>
    public long Revision { get; set; }

    public List<EmbedPage> Pages { get; set; } = new();

    /// <summary>
    /// The page index the embed starts on when loaded.
    /// </summary>
    public int StartPage { get; set; }

    /// <summary>
    /// If true, hides the page navigation arrows below the embed.
    /// </summary>
    public bool HideChangePageArrows { get; set; }

    /// <summary>
    /// If true (the default), a live update keeps the page the user
    /// is currently viewing instead of resetting to the start page.
    /// </summary>
    public bool KeepPageOnUpdate { get; set; } = true;

    /// <summary>
    /// Enumerates every item in every page, depth-first.
    /// </summary>
    public IEnumerable<EmbedItem> EnumerateItems()
    {
        foreach (var page in Pages)
        {
            foreach (var child in page.Children)
            {
                yield return child;
                foreach (var descendant in child.EnumerateDescendants())
                    yield return descendant;
            }
        }
    }

    /// <summary>
    /// Finds an item anywhere in the embed by Id, or null.
    /// </summary>
    public EmbedItem? FindItem(string id) =>
        EnumerateItems().FirstOrDefault(x => x.Id == id);

    /// <summary>
    /// Replaces the item whose Id matches the replacement's Id, anywhere in
    /// the embed. Returns true if a swap occurred.
    /// </summary>
    public bool ReplaceItem(EmbedItem replacement)
    {
        if (replacement.Id is null)
            return false;

        foreach (var page in Pages)
        {
            for (var i = 0; i < page.Children.Count; i++)
            {
                if (page.Children[i].Id == replacement.Id)
                {
                    page.Children[i] = replacement;
                    return true;
                }

                if (page.Children[i].TryReplaceDescendant(replacement))
                    return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A single page of an embed: an optional title and footer around a tree of items.
/// </summary>
public class EmbedPage
{
    public string? Title { get; set; }

    public string? Footer { get; set; }

    /// <summary>
    /// Inline CSS declarations applied to the title.
    /// </summary>
    public string? TitleStyle { get; set; }

    /// <summary>
    /// Inline CSS declarations applied to the footer.
    /// </summary>
    public string? FooterStyle { get; set; }

    /// <summary>
    /// Inline CSS declarations applied to the page container.
    /// </summary>
    public string? Style { get; set; }

    public List<EmbedItem> Children { get; set; } = new();
}
