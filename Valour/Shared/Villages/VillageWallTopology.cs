namespace Valour.Shared.Villages;

/// <summary>
/// Resolves the eight neighboring wall cells to the 47-shape layout used by
/// GameMaker-style blob autotiles. Diagonal neighbors only participate when
/// both adjoining cardinal neighbors exist, which removes impossible corners
/// and reduces all 256 raw masks to the authored 47 shapes.
/// </summary>
public static class VillageWallTopology
{
    public const string DefinitionPrefix = "wall:";
    public const int FrameCount = 47;

    public const byte North = 1;
    public const byte NorthEast = 2;
    public const byte East = 4;
    public const byte SouthEast = 8;
    public const byte South = 16;
    public const byte SouthWest = 32;
    public const byte West = 64;
    public const byte NorthWest = 128;

    private static readonly IReadOnlyDictionary<byte, int> FrameByMask =
        new Dictionary<byte, int>
        {
            [255] = 0, [127] = 1, [253] = 2, [125] = 3,
            [247] = 4, [119] = 5, [245] = 6, [117] = 7,
            [223] = 8, [95] = 9, [221] = 10, [93] = 11,
            [215] = 12, [87] = 13, [213] = 14, [85] = 15,
            [31] = 16, [29] = 17, [23] = 18, [21] = 19,
            [124] = 20, [116] = 21, [92] = 22, [84] = 23,
            [241] = 24, [209] = 25, [113] = 26, [81] = 27,
            [199] = 28, [71] = 29, [197] = 30, [69] = 31,
            [17] = 32, [68] = 33, [28] = 34, [20] = 35,
            [112] = 36, [80] = 37, [193] = 38, [65] = 39,
            [7] = 40, [5] = 41, [16] = 42, [4] = 43,
            [1] = 44, [64] = 45, [0] = 46,
        };

    public static byte NormalizeMask(int rawMask)
    {
        var mask = (byte)rawMask;
        if ((mask & (North | East)) != (North | East))
            mask &= unchecked((byte)~NorthEast);
        if ((mask & (East | South)) != (East | South))
            mask &= unchecked((byte)~SouthEast);
        if ((mask & (South | West)) != (South | West))
            mask &= unchecked((byte)~SouthWest);
        if ((mask & (West | North)) != (West | North))
            mask &= unchecked((byte)~NorthWest);
        return mask;
    }

    public static int ResolveFrame(int rawMask) =>
        FrameByMask[NormalizeMask(rawMask)];

    public static int ResolveFrame(
        Func<int, int, bool> hasWall,
        int x,
        int y)
    {
        var mask = 0;
        if (hasWall(x, y - 1)) mask |= North;
        if (hasWall(x + 1, y - 1)) mask |= NorthEast;
        if (hasWall(x + 1, y)) mask |= East;
        if (hasWall(x + 1, y + 1)) mask |= SouthEast;
        if (hasWall(x, y + 1)) mask |= South;
        if (hasWall(x - 1, y + 1)) mask |= SouthWest;
        if (hasWall(x - 1, y)) mask |= West;
        if (hasWall(x - 1, y - 1)) mask |= NorthWest;
        return ResolveFrame(mask);
    }

    public static string MakeDefinitionKey(string wallSetKey, int frame)
    {
        if (!IsValidWallSetKey(wallSetKey))
            throw new ArgumentException("Wall set keys may only contain letters, numbers, dots, dashes, and underscores.", nameof(wallSetKey));
        if (frame is < 0 or >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frame));
        return $"{DefinitionPrefix}{wallSetKey}:{frame}";
    }

    public static bool TryParseDefinitionKey(
        string? definitionKey,
        out string wallSetKey,
        out int frame)
    {
        wallSetKey = string.Empty;
        frame = 0;
        if (string.IsNullOrWhiteSpace(definitionKey) ||
            !definitionKey.StartsWith(DefinitionPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separator = definitionKey.LastIndexOf(':');
        if (separator <= DefinitionPrefix.Length ||
            !int.TryParse(definitionKey.AsSpan(separator + 1), out frame) ||
            frame is < 0 or >= FrameCount)
        {
            return false;
        }

        wallSetKey = definitionKey[DefinitionPrefix.Length..separator];
        if (IsValidWallSetKey(wallSetKey))
            return true;

        wallSetKey = string.Empty;
        frame = 0;
        return false;
    }

    public static bool IsWallDefinitionKey(string? definitionKey) =>
        TryParseDefinitionKey(definitionKey, out _, out _);

    public static bool IsValidWallSetKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        key.Length <= 64 &&
        key.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
}
