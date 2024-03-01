namespace Valour.Shared.Models.Themes;

/// <summary>
/// Theme meta allows for viewing theme information without the need for the full theme object.
/// </summary>
public interface ISharedThemeMeta
{
    public long Id { get; set; }
    public long AuthorId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ImageUrl { get; set; }
}