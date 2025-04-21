namespace Valour.Shared.Models;

public interface ISortable
{
    public static readonly SortableComparer Comparer = new SortableComparer();
    public static readonly SortableComparerDescending ComparerDescending = new SortableComparerDescending();
    
    public uint GetSortPosition();

    public static int Compare(ISortable x, ISortable y)
    {
        return x.GetSortPosition().CompareTo(y.GetSortPosition());
    }
    
    public static int Compare<T>(T x, T y) where T : ISortable
    {
        return x.GetSortPosition().CompareTo(y.GetSortPosition());
    }
    
    public static int CompareDescending(ISortable x, ISortable y)
    {
        return y.GetSortPosition().CompareTo(x.GetSortPosition());
    }
    
    public static int CompareDescending<T>(T x, T y) where T : ISortable
    {
        return y.GetSortPosition().CompareTo(x.GetSortPosition());
    }
}

public class SortableComparer : IComparer<ISortable>
{
    int IComparer<ISortable>.Compare(ISortable x, ISortable y)
    {
        return ISortable.Compare(x, y);
    }
}

public class SortableComparerDescending : IComparer<ISortable>
{
    int IComparer<ISortable>.Compare(ISortable x, ISortable y)
    {
        return ISortable.CompareDescending(x, y);
    }
}