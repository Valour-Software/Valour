using System.Runtime.InteropServices;

namespace Valour.Client.Photino;

public static class LinuxAppId
{
    private const string Id = "gg.valour.Valour";

    private const string GLib = "libglib-2.0.so.0";

    public static void Apply()
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            g_set_prgname(Id);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine($"Could not set the Linux app id: {ex.Message}");
        }
    }

    [DllImport(GLib)]
    private static extern void g_set_prgname([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
}
