namespace Valour.Server.Mapping;

public static class VillagePlotMapper
{
    public static VillagePlot ToModel(this Valour.Database.VillagePlot plot)
    {
        if (plot is null)
            return null;

        return new VillagePlot()
        {
            Id = plot.Id,
            PlanetId = plot.PlanetId,
            MapId = plot.MapId,
            Name = plot.Name,
            X = plot.X,
            Y = plot.Y,
            Width = plot.Width,
            Height = plot.Height,
            OwnerMemberId = plot.OwnerMemberId,
            EditMode = plot.EditMode,
            ForSale = plot.ForSale,
            Price = plot.Price,
        };
    }

    public static Valour.Database.VillagePlot ToDatabase(this VillagePlot plot)
    {
        if (plot is null)
            return null;

        return new Valour.Database.VillagePlot()
        {
            Id = plot.Id,
            PlanetId = plot.PlanetId,
            MapId = plot.MapId,
            Name = plot.Name,
            X = plot.X,
            Y = plot.Y,
            Width = plot.Width,
            Height = plot.Height,
            OwnerMemberId = plot.OwnerMemberId,
            EditMode = plot.EditMode,
            ForSale = plot.ForSale,
            Price = plot.Price,
        };
    }
}
