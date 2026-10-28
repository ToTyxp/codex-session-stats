using System.ComponentModel;
using AgentSpeed.App;
using AgentSpeed.Core.Traffic.Windows;

namespace AgentSpeed.Traffic;

/// <summary>
/// The traffic helper's installation for Settings (WINDOWS-SERVICE 5, 6): the service state from the SCM and registry,
/// and install, update and removal through one elevated run of the embedded helper each.
/// </summary>
internal sealed class WindowsTrafficHelperHost : ITrafficHelperHost
{
    public HelperInstallState Query()
    {
        var info = HelperServiceControl.Query();
        return new HelperInstallState(info.Exists, info.CurrentUserAllowed, info.Version, info.State == HelperServiceState.Running,
            EmbeddedHelper.IsAvailable);
    }

    public Task<HelperActionReport> InstallAsync(nint owner, CancellationToken cancellation) =>
        RunAsync(owner, install: true, cancellation);

    public Task<HelperActionReport> RemoveAsync(nint owner, CancellationToken cancellation) =>
        RunAsync(owner, install: false, cancellation);

    private static async Task<HelperActionReport> RunAsync(nint owner, bool install, CancellationToken cancellation)
    {
        if (EmbeddedHelper.ForCurrentArchitecture() is not { } image)
        {
            AppLog.Warn("This build carries no valid traffic helper.");
            return new HelperActionReport(HelperActionResultKind.Failed);
        }

        var result = install
            ? await HelperInstaller.InstallAsync(image.Bytes, image.Sha256, owner, cancellation).ConfigureAwait(true)
            : await HelperInstaller.RemoveAsync(image.Bytes, image.Sha256, owner, cancellation).ConfigureAwait(true);
        if (result.Outcome != HelperInstallOutcome.Done && result.Outcome != HelperInstallOutcome.Cancelled)
        {
            AppLog.Warn($"Traffic helper {(install ? "install" : "removal")} {result.Outcome}: {result.Message} (error {result.Win32Error})");
        }

        return result.Outcome switch
        {
            HelperInstallOutcome.Done => new HelperActionReport(HelperActionResultKind.Done),
            HelperInstallOutcome.Cancelled => new HelperActionReport(HelperActionResultKind.Cancelled),
            // The system's own text for the error, in the user's Windows language; the log keeps the diagnostic.
            _ => new HelperActionReport(HelperActionResultKind.Failed, result.Win32Error != 0 ? new Win32Exception(result.Win32Error).Message : null),
        };
    }
}
