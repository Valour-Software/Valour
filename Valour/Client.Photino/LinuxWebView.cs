using System.Runtime.InteropServices;

namespace Valour.Client.Photino;

/// <summary>
/// Adjusts WebKitGTK settings that Photino hard-codes when it creates the web
/// view on Linux.
/// </summary>
public static class LinuxWebView
{
    private const string Gtk = "libgtk-3.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string WebKit = "libwebkit2gtk-4.1.so.0";

    /// <summary>
    /// Photino turns on WebKit's mock capture devices, which replace the real
    /// microphones, cameras, and screens with test sources. Turning them off
    /// gives calls the real devices. Call this on the GTK thread after the
    /// window and its web view exist.
    /// </summary>
    public static void DisableMockCaptureDevices()
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            var webViewType = webkit_web_view_get_type();
            var windows = gtk_window_list_toplevels();
            var changed = 0;

            // A GList node is a data pointer followed by next and previous pointers.
            for (var node = windows; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size))
            {
                var child = gtk_bin_get_child(Marshal.ReadIntPtr(node));
                if (child == IntPtr.Zero || !g_type_check_instance_is_a(child, webViewType))
                    continue;

                webkit_settings_set_enable_mock_capture_devices(webkit_web_view_get_settings(child), false);
                changed++;
            }

            g_list_free(windows);
            if (changed == 0)
                Console.Error.WriteLine("No web view was found, so WebKit's mock capture devices are still on.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine($"Could not turn off WebKit's mock capture devices: {ex.Message}");
        }
    }

    [DllImport(Gtk)]
    private static extern IntPtr gtk_window_list_toplevels();

    [DllImport(Gtk)]
    private static extern IntPtr gtk_bin_get_child(IntPtr bin);

    [DllImport(GLib)]
    private static extern void g_list_free(IntPtr list);

    [DllImport(GObject)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_type_check_instance_is_a(IntPtr instance, nuint type);

    [DllImport(WebKit)]
    private static extern nuint webkit_web_view_get_type();

    [DllImport(WebKit)]
    private static extern IntPtr webkit_web_view_get_settings(IntPtr webView);

    [DllImport(WebKit)]
    private static extern void webkit_settings_set_enable_mock_capture_devices(
        IntPtr settings, [MarshalAs(UnmanagedType.Bool)] bool enabled);
}
