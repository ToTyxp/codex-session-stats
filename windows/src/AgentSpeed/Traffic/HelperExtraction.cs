namespace AgentSpeed.Traffic;

/// <summary>
/// <c>AgentSpeed.exe --extract-helper DIR</c> (WINDOWS-SERVICE 5, <c>install.ps1 -TrafficHelper</c>): writes the embedded
/// helper for this architecture to <c>DIR\AgentSpeedTrafficHelper.exe</c>, checks it against the embedded SHA-256, and
/// writes that embedded hash to <c>DIR\AgentSpeedTrafficHelper.exe.sha256</c> (lowercase hex), so the caller passes the
/// app's own value to the elevated helper rather than a hash of the file it is about to run. DIR must be an empty,
/// ordinary directory or not exist yet. Exit code 0 on success, 1 otherwise (nothing is left behind).
/// </summary>
internal static class HelperExtraction
{
    public const string Argument = "--extract-helper";

    /// <summary>The exit code when <paramref name="args"/> asks for an extraction; null for any other launch.</summary>
    public static int? TryRun(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], Argument, StringComparison.Ordinal))
        {
            return null;
        }

        return args.Length == 2 ? Extract(args[1]) : 1;
    }

    private static int Extract(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)
            || EmbeddedHelper.ForCurrentArchitecture() is not { } image)
        {
            return 1;
        }

        var createdDirectory = false;
        string? written = null;
        string? hashFile = null;
        try
        {
            if (File.Exists(directory))
            {
                return 1;
            }

            if (Directory.Exists(directory))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    return 1;
                }
            }
            else
            {
                Directory.CreateDirectory(directory);
                createdDirectory = true;
            }

            var target = Path.Combine(directory, EmbeddedHelper.FileName);
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                written = target;
                output.Write(image.Bytes);
                output.Flush(flushToDisk: true);
            }

            byte[] onDisk;
            using (var input = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length != image.Bytes.Length)
                {
                    throw new IOException("The extracted helper has the wrong size.");
                }

                onDisk = new byte[input.Length];
                input.ReadExactly(onDisk);
            }

            if (EmbeddedHelper.HashOf(onDisk) != image.Sha256)
            {
                throw new IOException("The extracted helper does not match its embedded hash.");
            }

            var hashPath = target + ".sha256";
            using (var output = new FileStream(hashPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                hashFile = hashPath;
                output.Write(System.Text.Encoding.ASCII.GetBytes(image.Sha256));
                output.Flush(flushToDisk: true);
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException)
        {
            CleanUp(written, hashFile, createdDirectory ? directory : null);
            return 1;
        }
    }

    private static void CleanUp(string? file, string? hashFile, string? directory)
    {
        try
        {
            foreach (var path in new[] { hashFile, file })
            {
                if (path is not null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the caller removes its temporary folder anyway.
        }
    }
}
