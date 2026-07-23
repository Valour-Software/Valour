namespace Valour.Shared.Models.Themes;

public class ThemeAssetInfo
{
    public long Id { get; set; }
    public long ThemeId { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public bool Animated { get; set; }
    public string AssetType { get; set; } = "image";
}
