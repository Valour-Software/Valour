namespace Valour.Shared.Models;

public interface ISortableModel
{
    public uint GetSortPosition();

    public static int Compare(ISortableModel x, ISortableModel y)
    {
        return x.GetSortPosition().CompareTo(y.GetSortPosition());
    }
    
    public static int Compare<T>(T x, T y) where T : ISortableModel
    {
        return x.GetSortPosition().CompareTo(y.GetSortPosition());
    }
}