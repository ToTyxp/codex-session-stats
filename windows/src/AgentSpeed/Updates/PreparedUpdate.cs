using System.Diagnostics;
using AgentSpeed.Presentation.Updates;

namespace AgentSpeed.Updates;

/// <summary>A downloaded, verified and extracted update in its private temporary folder.</summary>
internal sealed class PreparedUpdate(DirectoryInfo folder, string executable, ReleaseInfo release) : IDisposable
{
    public string Executable { get; } = executable;

    public ReleaseInfo Release { get; } = release;

    public void Dispose() => UpdateFiles.TryDeleteDirectory(folder.FullName);
}

/// <summary>Download → checksum → extraction → version check (UPDATES section 5, common steps and Windows payload).</summary>
internal sealed class UpdatePreparer(UpdateClient client)
{
    /// <param name="report">Phase and download progress (0..1), called on the caller's context.</param>
    /// <exception cref="UpdateException">Any failure; the temporary folder is already deleted.</exception>
    public async Task<PreparedUpdate> PrepareAsync(InstallPlan plan, Action<UpdatePhase, double> report, CancellationToken cancellationToken)
    {
        var folder = Directory.CreateTempSubdirectory("AgentSpeed-update-");
        try
        {
            var checksums = Path.Combine(folder.FullName, UpdatePackage.ChecksumsName);
            var package = Path.Combine(folder.FullName, "package.zip");

            report(UpdatePhase.Downloading, 0);
            await client.DownloadAsync(plan.Checksums, checksums, null, cancellationToken);
            var size = (double)plan.Package.Size;
            var last = -1.0;
            var downloading = true;
            var progress = new Progress<long>(bytes =>
            {
                // Reports are posted; one may arrive after the download finished.
                var fraction = Math.Clamp(bytes / size, 0, 1);
                if (Volatile.Read(ref downloading) && (fraction - last >= 0.005 || fraction >= 1))
                {
                    last = fraction;
                    report(UpdatePhase.Downloading, fraction);
                }
            });
            await client.DownloadAsync(plan.Package, package, progress, cancellationToken);
            Volatile.Write(ref downloading, false);

            report(UpdatePhase.Verifying, 1);
            var text = await File.ReadAllTextAsync(checksums, cancellationToken);
            var expected = ChecksumManifest.Find(text, plan.Package.Name)
                ?? throw new UpdateException(UpdateFailure.Checksum, $"{UpdatePackage.ChecksumsName} has no entry for {plan.Package.Name}.");
            var actual = await ChecksumManifest.ComputeAsync(package, cancellationToken);
            if (!ChecksumManifest.Matches(expected, actual))
            {
                throw new UpdateException(UpdateFailure.Checksum, $"SHA-256 of {plan.Package.Name} does not match.");
            }

            var payload = Directory.CreateDirectory(Path.Combine(folder.FullName, "payload")).FullName;
            var executable = await Task.Run(() => UpdatePackage.ExtractExecutable(package, payload), cancellationToken);
            TryDelete(package);
            VerifyVersion(executable, plan.Release.Version);
            return new PreparedUpdate(folder, executable, plan.Release);
        }
        catch (Exception ex) when (ex is not UpdateException and not OperationCanceledException)
        {
            UpdateFiles.TryDeleteDirectory(folder.FullName);
            throw new UpdateException(UpdateFailure.Invalid, "Preparing the update failed.", ex);
        }
        catch
        {
            UpdateFiles.TryDeleteDirectory(folder.FullName);
            throw;
        }
    }

    /// <summary>The payload's ProductVersion (without +metadata) must equal the release version.</summary>
    private static void VerifyVersion(string executable, SemanticVersion expected)
    {
        var info = FileVersionInfo.GetVersionInfo(executable);
        if (!SemanticVersion.TryParse(info.ProductVersion, out var actual) || actual != expected)
        {
            throw new UpdateException(UpdateFailure.Invalid,
                $"The package is version '{info.ProductVersion ?? "none"}', expected {expected}.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
