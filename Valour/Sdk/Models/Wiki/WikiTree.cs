using Valour.Shared.Models.Wiki;

namespace Valour.Sdk.Models.Wiki;

public class WikiTreeNode
{
    public PlanetWikiPage Doc { get; set; }
    public List<WikiTreeNode> Children { get; } = new();
}

/// <summary>
/// Pure tree helpers over a planet's flat, position-sorted docs store
/// </summary>
public static class WikiTree
{
    /// <summary>
    /// Builds the root-level tree from a flat list. Input order is preserved
    /// within each sibling group, so passing a position-sorted store yields a
    /// correctly ordered tree.
    /// </summary>
    public static List<WikiTreeNode> Build(IEnumerable<PlanetWikiPage> flatSorted)
    {
        var nodes = new Dictionary<long, WikiTreeNode>();
        var ordered = new List<WikiTreeNode>();

        foreach (var doc in flatSorted)
        {
            var node = new WikiTreeNode { Doc = doc };
            nodes[doc.Id] = node;
            ordered.Add(node);
        }

        var roots = new List<WikiTreeNode>();
        foreach (var node in ordered)
        {
            if (node.Doc.ParentId is not null &&
                nodes.TryGetValue(node.Doc.ParentId.Value, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots;
    }

    /// <summary>
    /// Pages in reading order (pre-order traversal), used for prev/next links
    /// </summary>
    public static List<PlanetWikiPage> FlattenPages(List<WikiTreeNode> roots)
    {
        var result = new List<PlanetWikiPage>();

        void Walk(List<WikiTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.Doc.IsFolder)
                    result.Add(node.Doc);
                Walk(node.Children);
            }
        }

        Walk(roots);
        return result;
    }

    /// <summary>
    /// Ancestor chain of a doc, outermost first — used for breadcrumbs and
    /// auto-expanding the tree to a selection
    /// </summary>
    public static List<PlanetWikiPage> GetAncestors(Planet planet, PlanetWikiPage doc)
    {
        var ancestors = new List<PlanetWikiPage>();

        var cursor = doc?.ParentId;
        var guard = 0;
        while (cursor is not null && guard++ <= ISharedPlanetWikiPage.MaxDepth)
        {
            if (!planet.WikiPages.TryGet(cursor.Value, out var parent))
                break;

            ancestors.Insert(0, parent);
            cursor = parent.ParentId;
        }

        return ancestors;
    }
}
