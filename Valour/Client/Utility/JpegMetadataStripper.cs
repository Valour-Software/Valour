namespace Valour.Client.Utility;

/// <summary>
/// Removes metadata segments (EXIF, XMP, ICC and friends) from JPEG bytes
/// without re-encoding, by dropping APP1–APP15 marker segments. Used before
/// direct-to-bucket uploads, where the server never sees the bytes and so
/// cannot strip location metadata the way CDN uploads do.
/// </summary>
public static class JpegMetadataStripper
{
    /// <summary>
    /// Returns the input with metadata segments removed. Non-JPEG or
    /// malformed input is returned unchanged.
    /// </summary>
    public static byte[] Strip(byte[] bytes)
    {
        // JPEG magic: FF D8 (SOI)
        if (bytes is null || bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return bytes;

        using var output = new MemoryStream(bytes.Length);
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);

        var pos = 2;
        while (pos + 3 < bytes.Length)
        {
            if (bytes[pos] != 0xFF)
                return bytes; // malformed — keep original rather than corrupt

            var marker = bytes[pos + 1];

            // Start of scan — entropy-coded data follows; copy the rest verbatim
            if (marker == 0xDA)
            {
                output.Write(bytes, pos, bytes.Length - pos);
                return output.ToArray();
            }

            // Standalone markers (RSTn, EOI) have no length field
            if (marker is >= 0xD0 and <= 0xD9)
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                pos += 2;
                continue;
            }

            var length = (bytes[pos + 2] << 8) | bytes[pos + 3];
            if (length < 2 || pos + 2 + length > bytes.Length)
                return bytes; // malformed

            // Drop APP1–APP15 (EXIF, XMP, ICC, ...); keep APP0 (JFIF) and
            // everything structural.
            var drop = marker is >= 0xE1 and <= 0xEF;
            if (!drop)
                output.Write(bytes, pos, 2 + length);

            pos += 2 + length;
        }

        return bytes; // never reached SOS — malformed; keep original
    }
}
