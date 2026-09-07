namespace Valour.Database.Seeds.Villages;

public static class VillageDefaultWorld
{
    private static readonly Lazy<string> Version1 = new(() =>
    {
        using var stream = typeof(VillageDefaultWorld).Assembly.GetManifestResourceStream(
            "Valour.Database.Seeds.Villages.default-world-v1.json")
            ?? throw new InvalidOperationException("The bundled default village is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    });

    public static string Version1Json => Version1.Value;
    public static string CurrentJson => Version1Json;
}
