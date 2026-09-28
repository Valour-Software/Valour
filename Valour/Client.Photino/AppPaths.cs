namespace Valour.Client.Photino;

/// <summary>Where the desktop app keeps its files.</summary>
public static class AppPaths
{
    /// <summary>
    /// $XDG_CONFIG_HOME/valour, usually ~/.config/valour. The directory is
    /// created on first use and is readable only by the current user.
    /// </summary>
    public static string ConfigDirectory { get; } = CreatePrivateDirectory(Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "valour"));

    private static string CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
