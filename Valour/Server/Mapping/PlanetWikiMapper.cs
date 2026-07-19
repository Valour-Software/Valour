namespace Valour.Server.Mapping;

public static class PlanetWikiMapper
{
    public static PlanetWikiPage ToModel(this Valour.Database.PlanetWikiPage doc)
    {
        if (doc is null)
            return null;

        return new PlanetWikiPage
        {
            Id = doc.Id,
            PlanetId = doc.PlanetId,
            ParentId = doc.ParentId,
            IsFolder = doc.IsFolder,
            Slug = doc.Slug,
            PreviousSlug = doc.PreviousSlug,
            Title = doc.Title,
            Position = doc.Position,
            IsPublished = doc.IsPublished,
            Version = doc.Version,
            TimeCreated = doc.TimeCreated,
            LastEdited = doc.LastEdited,
            CreatedByUserId = doc.CreatedByUserId,
            LastEditedByUserId = doc.LastEditedByUserId,
            ImportSource = doc.ImportSource,
        };
    }

    public static Valour.Shared.Models.Wiki.PlanetWikiRevision ToModel(
        this Valour.Database.PlanetWikiRevision revision,
        bool includeContent)
    {
        if (revision is null)
            return null;

        return new Valour.Shared.Models.Wiki.PlanetWikiRevision
        {
            Id = revision.Id,
            PageId = revision.PageId,
            PlanetId = revision.PlanetId,
            Title = revision.Title,
            Content = includeContent ? revision.Content : null,
            AuthorUserId = revision.AuthorUserId,
            TimeCreated = revision.TimeCreated,
            ImportSource = revision.ImportSource,
        };
    }
}
