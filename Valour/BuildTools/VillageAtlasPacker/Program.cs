using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Valour.BuildTools.VillageAtlasPacker;

if (args.Length != 3)
    throw new ArgumentException("Expected manifest path, private PNG path, and protected output path.");

using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
var manifest = document.RootElement;
var imageUrl = manifest.GetProperty("image").GetString() ?? string.Empty;
if (imageUrl.Split('?')[0] != "/_content/Valour.Client/media/villages/library-atlas.vtex.bin" ||
    manifest.GetProperty("wallSets").EnumerateArray().Any(wall => wall.GetProperty("Image").GetString() != imageUrl))
    throw new InvalidDataException("The official Village manifest and wall sets must reference the protected app atlas.");
var png = File.ReadAllBytes(args[1]);
var expectedHash = manifest.GetProperty("imageSha256").GetString();
if (!string.Equals(Convert.ToHexString(SHA256.HashData(png)), expectedHash, StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("The Village atlas does not match its manifest. Restore the matching private release package.");

var encoded = AtlasEnvelope.Encode(png);
if (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) != manifest.GetProperty("atlas").GetProperty("width").GetInt32() ||
    BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) != manifest.GetProperty("atlas").GetProperty("height").GetInt32())
    throw new InvalidDataException("The Village atlas dimensions do not match its manifest.");

if (!File.Exists(args[2]) || !File.ReadAllBytes(args[2]).AsSpan().SequenceEqual(encoded))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    var temporary = args[2] + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        File.WriteAllBytes(temporary, encoded);
        File.Move(temporary, args[2], overwrite: true);
    }
    finally
    {
        File.Delete(temporary);
    }
}
