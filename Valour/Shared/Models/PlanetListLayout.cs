namespace Valour.Shared.Models;

public sealed class PlanetListFolder
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
}

public sealed class PlanetListPlacement
{
    public long PlanetId { get; set; }
    public long? FolderId { get; set; }
    public int Position { get; set; }
}

public sealed class PlanetListLayout
{
    public List<PlanetListFolder> Folders { get; set; } = [];
    public List<PlanetListPlacement> Planets { get; set; } = [];
}

public sealed class CreatePlanetListFolderRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class RenamePlanetListFolderRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SavePlanetListLayoutRequest
{
    public List<PlanetListPlacement> Planets { get; set; } = [];
    public List<long> FolderIds { get; set; } = [];
}
