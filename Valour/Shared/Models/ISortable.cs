namespace Valour.Shared.Models;

public interface ISortable
{
    public static readonly SortableComparer Comparer = new SortableComparer();
    
    public uint GetSortPosition();

    public static int Compare(ISortable x, ISortable y)
    {
        return x.GetSortPosition().CompareTo(y.GetSortPosition());
    }
    
    public static int Compare<T>(T x, T y) where T : ISortable
    {
        return x.GetSortPosition().CompareTo(y.GetSortPosition());
    }
}

public class SortableComparer : IComparer<ISortable>
{
    int IComparer<ISortable>.Compare(ISortable x, ISortable y)
    {
        return ISortable.Compare(x, y);
    }
}