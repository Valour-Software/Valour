using System.Text;

namespace Valour.Client.Utility.Downloads;

/// <summary>
/// Makes file names that are safe to create on any host.
/// </summary>
public static class DownloadFileNames
{
    private const int MaxLength = 120;

    // Windows refuses these as file names, with or without an extension.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Reduces a name to a single plain file name. Folders, control characters,
    /// characters that some file systems refuse, and reserved names are removed
    /// or replaced, and very long names are shortened while keeping the extension.
    /// </summary>
    public static string Sanitize(string fileName, string fallback = "download")
    {
        var name = fileName ?? string.Empty;

        // Anything before the last separator is a folder the server or a
        // message author must not choose for the person.
        var separator = name.LastIndexOfAny(['/', '\\']);
        if (separator >= 0)
            name = name[(separator + 1)..];

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsControl(c) || "<>:\"|?*".Contains(c) ? '_' : c);
        }

        name = builder.ToString().Trim().Trim('.').Trim();
        if (name.Length == 0)
            return fallback;

        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);

        if (ReservedNames.Contains(stem))
            stem = "_" + stem;

        if (stem.Length + extension.Length > MaxLength)
        {
            // An extension longer than this is not worth keeping.
            if (extension.Length > 16)
                extension = string.Empty;

            stem = stem[..Math.Max(1, MaxLength - extension.Length)];
        }

        return stem + extension;
    }

    /// <summary>
    /// Creates a new file in the folder, adding " (1)", " (2)" and so on to the
    /// name when one already exists, so a download never replaces another file.
    /// </summary>
    public static FileStream CreateUnique(string directory, string fileName, out string path)
    {
        Directory.CreateDirectory(directory);

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var attempt = 0; ; attempt++)
        {
            var candidate = attempt == 0 ? fileName : $"{stem} ({attempt}){extension}";
            var candidatePath = Path.Combine(directory, candidate);

            try
            {
                // CreateNew fails if the file exists, even when another download
                // creates it at the same moment.
                var stream = new FileStream(candidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);
                path = candidatePath;
                return stream;
            }
            catch (IOException) when (File.Exists(candidatePath))
            {
                if (attempt >= 999)
                    throw;
            }
        }
    }
}
