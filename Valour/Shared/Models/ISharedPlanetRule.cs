namespace Valour.Shared.Models;

public interface ISharedPlanetRule : ISharedPlanetModel<long>, ISortable
{
    public static string GetBaseRoute(long planetId) => $"api/planets/{planetId}/rules";
    public static string GetIdRoute(long planetId, long id) => $"{GetBaseRoute(planetId)}/{id}";

    public const int MaxTitleLength = 100;
    public const int MaxDescriptionLength = 2000;

    /// <summary>
    /// Lower values render first.
    /// </summary>
    uint Position { get; set; }

    /// <summary>
    /// Short title for the rule.
    /// </summary>
    string Title { get; set; }

    /// <summary>
    /// Markdown-capable rule details.
    /// </summary>
    string Description { get; set; }

    uint ISortable.GetSortPosition()
    {
        return Position;
    }
}
