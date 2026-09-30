using System.Runtime.InteropServices;
using Valour.Sdk.E2ee;

namespace Valour.Client.Photino.Storage;

/// <summary>
/// Stores end-to-end encryption keys with the desktop's Secret Service
/// (GNOME Keyring or KWallet) through libsecret. When no Secret Service is
/// available, keys go to a file that only the current user can read.
/// The choice is made once, on first use, so keys never split across both.
/// </summary>
public sealed class SecretServiceKeyStore : IE2eeKeyStore
{
    private readonly Lazy<IE2eeKeyStore> _store = new(Choose);

    public Task<byte[]> GetAsync(string key) => _store.Value.GetAsync(key);

    public Task SetAsync(string key, byte[] value) => _store.Value.SetAsync(key, value);

    public Task RemoveAsync(string key) => _store.Value.RemoveAsync(key);

    private static IE2eeKeyStore Choose()
    {
        var fileStore = new PrivateFileKeyStore(Path.Combine(AppPaths.ConfigDirectory, "keys"));

        // Keys already on disk stay there, even if a keyring appears later.
        if (!OperatingSystem.IsLinux() || fileStore.HasKeys)
            return fileStore;

        if (LibSecret.TryCreate(out var secret, out var reason))
        {
            Console.WriteLine("Encryption keys are stored in the Secret Service.");
            return secret;
        }

        Console.Error.WriteLine($"Secret Service unavailable ({reason}). Encryption keys are stored in {fileStore.Directory}.");
        return fileStore;
    }

    private sealed class LibSecret : IE2eeKeyStore
    {
        private const string LibSecretName = "libsecret-1.so.0";
        private const string GLibName = "libglib-2.0.so.0";
        private const string SchemaName = "gg.valour.E2eeKey";
        private const string KeyAttribute = "key";
        private const string ProbeKey = "valour-probe";

        // SecretSchema is a name, flags, 32 attribute slots, and reserved fields.
        private const int SchemaSize = 8 + 8 + 32 * 16 + 8 + 7 * 8;

        private readonly IntPtr _schema;
        private readonly IntPtr _strHash;
        private readonly IntPtr _strEqual;

        private LibSecret(IntPtr schema, IntPtr strHash, IntPtr strEqual)
        {
            _schema = schema;
            _strHash = strHash;
            _strEqual = strEqual;
        }

        public static bool TryCreate(out IE2eeKeyStore store, out string reason)
        {
            store = null!;
            try
            {
                var glib = NativeLibrary.Load(GLibName);
                var schema = Marshal.AllocHGlobal(SchemaSize);
                for (var offset = 0; offset < SchemaSize; offset += 8)
                    Marshal.WriteInt64(schema, offset, 0);
                Marshal.WriteIntPtr(schema, 0, Marshal.StringToCoTaskMemUTF8(SchemaName));
                Marshal.WriteIntPtr(schema, 16, Marshal.StringToCoTaskMemUTF8(KeyAttribute));
                // Flags (offset 8) and the attribute type (offset 24) are both
                // zero: no flags, and a string attribute.

                var candidate = new LibSecret(schema,
                    NativeLibrary.GetExport(glib, "g_str_hash"),
                    NativeLibrary.GetExport(glib, "g_str_equal"));

                // Storing a throwaway item fails here when no Secret Service is
                // running or it has no default collection to store into.
                candidate.SetAsync(ProbeKey, [0]);
                candidate.RemoveAsync(ProbeKey);
                store = candidate;
                reason = null!;
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                reason = ex.Message;
                return false;
            }
        }

        public Task<byte[]> GetAsync(string key)
        {
            var value = Lookup(key);
            if (string.IsNullOrEmpty(value))
                return Task.FromResult<byte[]>(null!);

            try
            {
                return Task.FromResult(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return Task.FromResult<byte[]>(null!);
            }
        }

        public Task SetAsync(string key, byte[] value)
        {
            WithAttributes(key, attributes =>
            {
                var ok = secret_password_storev_sync(_schema, attributes, null, $"Valour encryption key ({key})",
                    Convert.ToBase64String(value), IntPtr.Zero, out var error);
                ThrowIfError(error);
                if (!ok)
                    throw new InvalidOperationException("The Secret Service did not store the key.");
            });
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            WithAttributes(key, attributes =>
            {
                secret_password_clearv_sync(_schema, attributes, IntPtr.Zero, out var error);
                ThrowIfError(error);
            });
            return Task.CompletedTask;
        }

        private string? Lookup(string key)
        {
            string? result = null;
            WithAttributes(key, attributes =>
            {
                var password = secret_password_lookupv_sync(_schema, attributes, IntPtr.Zero, out var error);
                ThrowIfError(error);
                if (password != IntPtr.Zero)
                {
                    result = Marshal.PtrToStringUTF8(password);
                    secret_password_free(password);
                }
            });
            return result;
        }

        private void WithAttributes(string key, Action<IntPtr> action)
        {
            var name = Marshal.StringToCoTaskMemUTF8(KeyAttribute);
            var value = Marshal.StringToCoTaskMemUTF8(key);
            var table = g_hash_table_new(_strHash, _strEqual);
            try
            {
                g_hash_table_insert(table, name, value);
                action(table);
            }
            finally
            {
                g_hash_table_unref(table);
                Marshal.FreeCoTaskMem(name);
                Marshal.FreeCoTaskMem(value);
            }
        }

        private static void ThrowIfError(IntPtr error)
        {
            if (error == IntPtr.Zero)
                return;

            // GError is a quark, an int code, and then the message pointer.
            var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8));
            g_error_free(error);
            throw new InvalidOperationException(message);
        }

        [DllImport(LibSecretName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool secret_password_storev_sync(IntPtr schema, IntPtr attributes,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? collection,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string password,
            IntPtr cancellable, out IntPtr error);

        [DllImport(LibSecretName)]
        private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes,
            IntPtr cancellable, out IntPtr error);

        [DllImport(LibSecretName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool secret_password_clearv_sync(IntPtr schema, IntPtr attributes,
            IntPtr cancellable, out IntPtr error);

        [DllImport(LibSecretName)]
        private static extern void secret_password_free(IntPtr password);

        [DllImport(GLibName)]
        private static extern IntPtr g_hash_table_new(IntPtr hashFunc, IntPtr equalFunc);

        [DllImport(GLibName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

        [DllImport(GLibName)]
        private static extern void g_hash_table_unref(IntPtr table);

        [DllImport(GLibName)]
        private static extern void g_error_free(IntPtr error);
    }

    /// <summary>One file per key in a directory only the current user can open.</summary>
    private sealed class PrivateFileKeyStore : IE2eeKeyStore
    {
        public PrivateFileKeyStore(string directory)
        {
            Directory = directory;
        }

        public string Directory { get; }

        public bool HasKeys => System.IO.Directory.Exists(Directory) &&
                               System.IO.Directory.EnumerateFiles(Directory).Any();

        public async Task<byte[]> GetAsync(string key)
        {
            var path = PathFor(key);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null!;
        }

        public async Task SetAsync(string key, byte[] value)
        {
            if (OperatingSystem.IsWindows())
                System.IO.Directory.CreateDirectory(Directory);
            else
                System.IO.Directory.CreateDirectory(Directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var path = PathFor(key);
            var temp = path + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using (var stream = new FileStream(temp, options))
                await stream.WriteAsync(value);
            File.Move(temp, path, overwrite: true);
        }

        public Task RemoveAsync(string key)
        {
            File.Delete(PathFor(key));
            return Task.CompletedTask;
        }

        // Key names are app-defined, but hex keeps any character out of the path.
        private string PathFor(string key) =>
            Path.Combine(Directory, Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(key)));
    }
}
