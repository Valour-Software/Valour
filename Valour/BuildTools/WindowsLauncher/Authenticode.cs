using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Valour.WindowsLauncher;

/// <summary>
/// Reads and verifies Authenticode signatures on Windows executables.
/// </summary>
internal static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckNone = 0x10;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

    /// <summary>
    /// Returns true when the file's signature is intact and chains to a trusted
    /// root. Revocation is not checked, so verification works offline.
    /// </summary>
    public static bool IsSignatureValid(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfoPtr = IntPtr.Zero;

        try
        {
            var fileInfo = new WintrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
                pcwszFilePath = pathPtr
            };

            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdRevocationCheckNone | WtdCacheOnlyUrlRetrieval
            };

            var action = GenericVerifyV2;
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);

            data.dwStateAction = WtdStateActionClose;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);

            return result == 0;
        }
        finally
        {
            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }

            Marshal.FreeHGlobal(pathPtr);
        }
    }

    /// <summary>
    /// Returns the subject of the certificate embedded in the file's signature,
    /// or null when the file is unsigned. This does not verify the signature.
    /// </summary>
    public static string? TryGetSignerSubject(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return certificate.Subject;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the length of a signed PE image through the end of its
    /// certificate table, or null when the stream is not a signed PE file.
    /// Bytes past this length are not part of the signed image.
    /// </summary>
    public static long? GetSignedImageLength(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

            stream.Seek(0x3C, SeekOrigin.Begin);
            var peHeaderOffset = reader.ReadInt32();

            stream.Seek(peHeaderOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550)
            {
                return null;
            }

            // The optional header follows the 20-byte file header. Its data
            // directories start at 96 bytes (PE32) or 112 bytes (PE32+), and
            // the certificate table is directory entry 4.
            var optionalHeaderOffset = peHeaderOffset + 4 + 20;
            stream.Seek(optionalHeaderOffset, SeekOrigin.Begin);
            var magic = reader.ReadUInt16();
            var dataDirectoryOffset = optionalHeaderOffset + (magic == 0x20B ? 112 : 96);

            stream.Seek(dataDirectoryOffset + 4 * 8, SeekOrigin.Begin);
            var certificateTableOffset = reader.ReadUInt32();
            var certificateTableSize = reader.ReadUInt32();

            if (certificateTableOffset == 0 || certificateTableSize == 0)
            {
                return null;
            }

            var end = (long)certificateTableOffset + certificateTableSize;
            return end <= stream.Length ? end : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionId, ref WintrustData pWvtData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
