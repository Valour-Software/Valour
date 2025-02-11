namespace Valour.Server.Utilities;

public class StartupWaitFlag
{
    private static int _shouldWaitFlags = 0;

    public static int Value => _shouldWaitFlags;

    public static int Increment()
    {
        return Interlocked.Increment(ref _shouldWaitFlags);
    }

    public static int Decrement()
    {
        return Interlocked.Decrement(ref _shouldWaitFlags);
    }
}
