using System.Buffers.Binary;

namespace Valour.BuildTools.VillageAtlasPacker;

public static class AtlasEnvelope
{
    public const int MaximumImageBytes = 16 * 1024 * 1024;

    public static byte[] Encode(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 24 || png.Length > MaximumImageBytes || !png[..8].SequenceEqual(signature))
            throw new InvalidDataException("The official atlas must be a PNG no larger than 16 MiB.");

        uint checksum = 2166136261;
        foreach (var value in png) checksum = unchecked((checksum ^ value) * 16777619);
        var seed = checksum ^ 0x9e3779b9;
        if (seed == 0) seed = 1;
        var output = new byte[png.Length + 20];
        "VLTEX001"u8.CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), png.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12), checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(16), seed);
        for (var i = 0; i < png.Length; i++)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            output[i + 20] = (byte)(png[i] ^ (seed & 255));
        }
        return output;
    }
}
