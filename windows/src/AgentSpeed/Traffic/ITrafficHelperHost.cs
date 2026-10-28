using AgentSpeed.Core.Traffic.Helper;

namespace AgentSpeed.Traffic;

/// <summary>What Settings shows about the traffic helper's installation (WINDOWS-SERVICE 2, 6).</summary>
/// <param name="Installed">The service and its registry key exist.</param>
/// <param name="Allowed">The current user is among the allowed users.</param>
/// <param name="Version">The installed helper's version, when readable.</param>
/// <param name="Running">The service runs now.</param>
/// <param name="Bundled">This build carries a helper it can install.</param>
internal readonly record struct HelperInstallState(bool Installed, bool Allowed, HelperVersion? Version, bool Running, bool Bundled)
{
    public static HelperInstallState Unknown => new(false, false, null, false, false);
}

internal enum HelperActionResultKind
{
    Done,

    /// <summary>The administrator prompt was cancelled; nothing changed.</summary>
    Cancelled,

    Failed,
}

/// <param name="Message">The failure's text (a Win32 message), shown after <c>traffic.helper.error</c>.</param>
internal readonly record struct HelperActionReport(HelperActionResultKind Result, string? Message = null);

/// <summary>Installs, updates and removes the traffic helper with one administrator prompt each (WINDOWS-SERVICE 5).</summary>
internal interface ITrafficHelperHost
{
    /// <summary>A cheap query of the service and its registry key; never throws.</summary>
    HelperInstallState Query();

    /// <summary>Install or update: stage the embedded helper, verify it, run it elevated, start the service.</summary>
    Task<HelperActionReport> InstallAsync(nint owner, CancellationToken cancellation);

    /// <summary>Remove this user (and the service when no allowed user remains).</summary>
    Task<HelperActionReport> RemoveAsync(nint owner, CancellationToken cancellation);
}
