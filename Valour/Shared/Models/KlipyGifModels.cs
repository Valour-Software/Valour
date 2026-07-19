using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

/// <summary>
/// The normalized Klipy search response used by the client UI. Provider
/// credentials are never part of the response model; the public platform key
/// is configured separately by the host application.
/// </summary>
public sealed class KlipyGifSearchResults
{
    public List<KlipyGif> Results { get; set; } = new();
    public bool HasNext { get; set; }
}

public sealed class KlipyCategory
{
    public string Name { get; set; } = string.Empty;
    public string SearchTerm { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
}

public sealed class KlipyGif
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public KlipyMediaFile File { get; set; } = new();

    [JsonIgnore]
    public KlipyMediaAsset? Preview =>
        File.Sm?.Webp ?? File.Sm?.Gif ??
        File.Xs?.Webp ?? File.Xs?.Gif ??
        File.Md?.Webp ?? File.Md?.Gif ??
        File.Hd?.Webp ?? File.Hd?.Gif;

    [JsonIgnore]
    public KlipyMediaAsset? Gif =>
        File.Md?.Gif ?? File.Hd?.Gif ?? File.Sm?.Gif ?? File.Xs?.Gif;
}

public sealed class KlipyMediaFile
{
    public KlipyMediaFormats? Hd { get; set; }
    public KlipyMediaFormats? Md { get; set; }
    public KlipyMediaFormats? Sm { get; set; }
    public KlipyMediaFormats? Xs { get; set; }
}

public sealed class KlipyMediaFormats
{
    public KlipyMediaAsset? Gif { get; set; }
    public KlipyMediaAsset? Webp { get; set; }
    public KlipyMediaAsset? Mp4 { get; set; }
}

public sealed class KlipyMediaAsset
{
    public string Url { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long Size { get; set; }
}
