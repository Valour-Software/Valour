namespace Valour.Shared.Models;

public interface ISortableModel
{
    public int GetSortPosition();

    public static int Compare(ISortableModel x, ISortableModel y)
    {
        return x.GetSortPosition().CompareTo(y.GetSortPosition());
    }
}