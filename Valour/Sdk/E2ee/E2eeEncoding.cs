using System.Buffers.Binary;
using System.Text;

namespace Valour.Sdk.E2ee;

/// <summary>
/// Raised when signed or encrypted end-to-end data is malformed.
/// </summary>
public sealed class E2eeFormatException : Exception
{
    public E2eeFormatException(string message) : base(message) { }
}

/// <summary>
/// Raised when a signature, authority rule, or authenticated decryption fails.
/// </summary>
public sealed class E2eeVerificationException : Exception
{
    public E2eeVerificationException(string message) : base(message) { }
}

/// <summary>
/// Writes the canonical binary form used for everything that is signed or
/// used as authenticated data. Every client and the server must produce the
/// same bytes for the same values, so integers are big-endian and variable
/// fields are length-prefixed.
/// </summary>
public sealed class E2eeWriter
{
    private readonly MemoryStream _stream = new();

    public E2eeWriter WriteMagic(string magic)
    {
        var bytes = Encoding.ASCII.GetBytes(magic);
        _stream.Write(bytes);
        return this;
    }

    public E2eeWriter WriteByte(byte value)
    {
        _stream.WriteByte(value);
        return this;
    }

    public E2eeWriter WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public E2eeWriter WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        _stream.Write(buffer);
        return this;
    }

    public E2eeWriter WriteInt64(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        _stream.Write(buffer);
        return this;
    }

    /// <summary>
    /// Writes a fixed-length field. The reader must know the length.
    /// </summary>
    public E2eeWriter WriteFixed(ReadOnlySpan<byte> value, int expectedLength)
    {
        if (value.Length != expectedLength)
            throw new E2eeFormatException($"Expected {expectedLength} bytes but got {value.Length}.");
        _stream.Write(value);
        return this;
    }

    public E2eeWriter WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteInt32(value.Length);
        _stream.Write(value);
        return this;
    }

    public E2eeWriter WriteString(string value)
    {
        var bytes = value is null ? [] : Encoding.UTF8.GetBytes(value);
        return WriteBytes(bytes);
    }

    public E2eeWriter WriteInt64List(IReadOnlyCollection<long> values)
    {
        WriteInt32(values.Count);
        foreach (var value in values)
            WriteInt64(value);
        return this;
    }

    public byte[] ToArray() => _stream.ToArray();
}

/// <summary>
/// Reads the canonical binary form written by <see cref="E2eeWriter"/>.
/// All reads are bounds checked because the input may come from an untrusted
/// server or peer.
/// </summary>
public sealed class E2eeReader
{
    private const int MaxFieldLength = 1024 * 1024;

    private readonly byte[] _data;
    private int _offset;

    public E2eeReader(byte[] data)
    {
        _data = data ?? throw new E2eeFormatException("Missing data.");
    }

    public bool AtEnd => _offset == _data.Length;

    public void ReadMagic(string magic)
    {
        var expected = Encoding.ASCII.GetBytes(magic);
        var actual = Take(expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new E2eeFormatException($"Unexpected record type. Expected {magic}.");
    }

    public byte ReadByte() => Take(1)[0];

    public bool ReadBool()
    {
        var value = ReadByte();
        if (value > 1)
            throw new E2eeFormatException("Invalid boolean value.");
        return value == 1;
    }

    public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

    public long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(Take(8));

    public byte[] ReadFixed(int length) => Take(length).ToArray();

    public byte[] ReadBytes()
    {
        var length = ReadInt32();
        if (length < 0 || length > MaxFieldLength)
            throw new E2eeFormatException("Invalid field length.");
        return Take(length).ToArray();
    }

    public string ReadString()
    {
        var bytes = ReadBytes();
        return Encoding.UTF8.GetString(bytes);
    }

    public List<long> ReadInt64List()
    {
        var count = ReadInt32();
        if (count < 0 || count > MaxFieldLength / 8)
            throw new E2eeFormatException("Invalid list length.");
        var list = new List<long>(count);
        for (var i = 0; i < count; i++)
            list.Add(ReadInt64());
        return list;
    }

    public void EnsureEnd()
    {
        if (!AtEnd)
            throw new E2eeFormatException("Unexpected trailing data.");
    }

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || _offset + length > _data.Length)
            throw new E2eeFormatException("Unexpected end of data.");
        var span = new ReadOnlySpan<byte>(_data, _offset, length);
        _offset += length;
        return span;
    }
}

/// <summary>
/// URL-safe base64 without padding, used for identifiers and QR payloads.
/// </summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        if (value is null)
            throw new E2eeFormatException("Missing base64 value.");
        var normalized = value.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
            case 1: throw new E2eeFormatException("Invalid base64 value.");
        }

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            throw new E2eeFormatException("Invalid base64 value.");
        }
    }
}
