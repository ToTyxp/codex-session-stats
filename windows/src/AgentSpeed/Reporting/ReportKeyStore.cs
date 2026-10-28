using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AgentSpeed.App;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Reporting;

/// <summary>
/// Device keys and API keys of live reporting (DESIGN 11, Storage on Windows): <c>%LOCALAPPDATA%\AgentSpeed\report\&lt;deviceId&gt;.key</c>
/// (the PKCS#8 private key) and <c>&lt;deviceId&gt;.apikey</c> (UTF-8), each protected with DPAPI for the current user, the device
/// id's 16 bytes as additional entropy and only <c>CRYPTPROTECT_UI_FORBIDDEN</c>: readable whenever the user is signed in,
/// the locked screen included, never prompting. Written to a temporary file and renamed. Nothing here is ever logged.
/// </summary>
internal sealed partial class ReportKeyStore
{
    private const int CryptProtectUiForbidden = 0x1;

    public ReportKeyStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>
    /// <c>%LOCALAPPDATA%\AgentSpeed\report</c>; the developer variable <c>AGENT_SPEED_REPORT_DIR</c> (a fully qualified local path)
    /// gives a test session its own keys.
    /// </summary>
    public static ReportKeyStore Standard()
    {
        var custom = Environment.GetEnvironmentVariable("AGENT_SPEED_REPORT_DIR");
        var directory = custom is { Length: > 0 } && Path.IsPathFullyQualified(custom) && !custom.StartsWith(@"\\", StringComparison.Ordinal)
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ProductFolder, "report");
        return new ReportKeyStore(directory);
    }

    /// <summary>Creates a key in the current or pending slot, replacing what was there.</summary>
    /// <exception cref="CryptographicException">The key could not be created or stored.</exception>
    public SoftwareReportSigner CreateKey(string deviceId, bool pending = false)
    {
        var path = File(deviceId, pending ? "pending.key" : "key");
        var signer = new SoftwareReportSigner();
        var pkcs8 = signer.ExportPkcs8();
        try
        {
            Write(path, Protect(pkcs8, deviceId));
            return signer;
        }
        catch
        {
            signer.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    public SoftwareReportSigner? LoadKey(string deviceId, bool pending = false)
    {
        if (!ReportCanonical.IsDeviceId(deviceId))
        {
            return null;
        }

        byte[]? plain = null;
        try
        {
            var path = File(deviceId, pending ? "pending.key" : "key");
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            plain = Unprotect(System.IO.File.ReadAllBytes(path), deviceId);
            return plain is null ? null : SoftwareReportSigner.FromPkcs8(plain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            AppLog.Warn("Live reporting: a stored device key could not be read.");
            return null;
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    /// <summary>The pending key becomes the current one (REPORTING 4.5).</summary>
    public bool PromotePending(string deviceId)
    {
        var pending = File(deviceId, "pending.key");
        try
        {
            if (!System.IO.File.Exists(pending))
            {
                return false;
            }

            System.IO.File.Move(pending, File(deviceId, "key"), overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void DeletePending(string deviceId) => Delete(File(deviceId, "pending.key"));

    /// <summary><i>Forget</i>, <i>Reset</i> and a locally forgotten identity: every key and the API key.</summary>
    public void DeleteAll(string deviceId)
    {
        if (!ReportCanonical.IsDeviceId(deviceId))
        {
            return;
        }

        foreach (var suffix in new[] { "key", "pending.key", "apikey" })
        {
            Delete(File(deviceId, suffix));
        }
    }

    /// <summary>Whether any file of the device is on disk (the e2e harness checks <i>Forget</i> leaves nothing).</summary>
    public bool HasFiles(string deviceId) =>
        ReportCanonical.IsDeviceId(deviceId) && new[] { "key", "pending.key", "apikey" }.Any(s => System.IO.File.Exists(File(deviceId, s)));

    /// <exception cref="CryptographicException">The key could not be stored.</exception>
    public void SaveApiKey(string apiKey, string deviceId)
    {
        var plain = Encoding.UTF8.GetBytes(apiKey);
        try
        {
            Write(File(deviceId, "apikey"), Protect(plain, deviceId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public string? LoadApiKey(string deviceId)
    {
        try
        {
            var path = File(deviceId, "apikey");
            if (!System.IO.File.Exists(path) || Unprotect(System.IO.File.ReadAllBytes(path), deviceId) is not { } plain)
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    public void DeleteApiKey(string deviceId) => Delete(File(deviceId, "apikey"));

    /// <summary>Device ids are our own lower-case UUIDs; anything else never becomes a path.</summary>
    private string File(string deviceId, string suffix)
    {
        if (!ReportCanonical.IsDeviceId(deviceId))
        {
            throw new CryptographicException("Invalid device id.");
        }

        return Path.Combine(Directory, $"{deviceId}.{suffix}");
    }

    private void Write(string path, byte[] data)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temporary = Path.Combine(Directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            System.IO.File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CryptographicException("The key could not be stored.", ex);
        }
        finally
        {
            Delete(temporary);
        }
    }

    private static void Delete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The device id's 16 bytes in the order they are written.</summary>
    private static byte[] Entropy(string deviceId) => Convert.FromHexString(deviceId.Replace("-", string.Empty, StringComparison.Ordinal));

    private static unsafe byte[] Protect(byte[] plain, string deviceId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new CryptographicException("DPAPI is only available on Windows.");
        }

        var entropy = Entropy(deviceId);
        fixed (byte* data = plain)
        fixed (byte* salt = entropy)
        {
            var input = new DataBlob { Length = plain.Length, Data = (nint)data };
            var extra = new DataBlob { Length = entropy.Length, Data = (nint)salt };
            if (!CryptProtectData(ref input, 0, ref extra, 0, 0, CryptProtectUiForbidden, out var output))
            {
                throw new CryptographicException("CryptProtectData failed.");
            }

            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
    }

    private static unsafe byte[]? Unprotect(byte[] sealedData, string deviceId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var entropy = Entropy(deviceId);
        fixed (byte* data = sealedData)
        fixed (byte* salt = entropy)
        {
            var input = new DataBlob { Length = sealedData.Length, Data = (nint)data };
            var extra = new DataBlob { Length = entropy.Length, Data = (nint)salt };
            if (!CryptUnprotectData(ref input, 0, ref extra, 0, 0, CryptProtectUiForbidden, out var output))
            {
                return null;
            }

            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                new Span<byte>((void*)output.Data, output.Length).Clear();
                return result;
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public nint Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob dataIn, nint description, ref DataBlob entropy, nint reserved, nint prompt,
        int flags, out DataBlob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob dataIn, nint description, ref DataBlob entropy, nint reserved, nint prompt,
        int flags, out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
