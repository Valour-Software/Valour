using System.Text.Json.Serialization;

namespace Valour.Shared.Villages;

public sealed class VillageTemplateStatus
{
    public int Revision { get; set; }
    public int ResetBeforeRevision { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? DraftPlanetId { get; set; }
    public string? DraftPlanetName { get; set; }
    public int DraftMapCount { get; set; }
    public int PublishedMapCount { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public bool IsBuiltIn { get; set; }
}

public sealed class VillageTemplateDraftRequest
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long PlanetId { get; set; }
    public int ExpectedRevision { get; set; }
}

public sealed class VillageTemplatePublishRequest
{
    public int ExpectedRevision { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long DraftPlanetId { get; set; }
    public bool ResetExisting { get; set; }
}
