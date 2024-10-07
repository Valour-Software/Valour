namespace Valour.Shared.Models;

public interface IOrderedModel
{
    public int Position { get; set; }

    public int Compare(IOrderedModel x, IOrderedModel y)
    {
        return x.Position.CompareTo(y.Position);
    }
}