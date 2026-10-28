using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Etw;
using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Service;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Install;

/// <summary>
/// The elevated <c>install</c> and <c>remove</c> verbs (WINDOWS-SERVICE 5). Each runs once in its own elevated process
/// and exits 0 (ok), 1 (refused: a check failed) or 2 (failed). The service is not started here; the app starts it.
/// </summary>
internal static class Installer
{
    public static HelperExitCode Install(string userSid, byte[] expectedSha256)
    {
        if (!Tokens.IsElevatedAdministrator())
        {
            return Refuse("install needs an elevated administrator token");
        }

        if (!AccountLookup.IsUserAccount(userSid, out var accountError))
        {
            return Refuse($"--user is not a user account (a group, an alias, or unknown to this computer; error {accountError})");
        }

        if (!UserConfirmation.Confirms(userSid, out var how))
        {
            return Refuse($"cannot confirm the requesting user: {how}");
        }

        var self = SelfImage.Path();
        if (self is null)
        {
            return Fail("cannot resolve this executable's path", Kernel32.LastError);
        }

        var image = SelfImage.Read(self, out var selfIdentity, out var error);
        if (image is null)
        {
            return Fail("cannot read this executable", error);
        }

        Span<byte> actual = stackalloc byte[32];
        if (!SystemLibraries.Sha256(image, actual))
        {
            return Fail("cannot hash this executable", 0);
        }

        if (!actual.SequenceEqual(expectedSha256))
        {
            return Refuse("this executable does not match the expected SHA-256");
        }

        var programFiles = HelperRegistry.ProgramFilesDirectory();
        if (programFiles is null)
        {
            return Fail("cannot resolve the Program Files folder", 0);
        }

        var folder = InstallFolder.Prepare(programFiles, out error);
        if (folder != StepResult.Ok)
        {
            return folder == StepResult.Refused
                ? Refuse($"the install folder is not safe (redirected, not owned by administrators, or writable by others): {HelperProtocol.InstallFolder(programFiles)}")
                : Fail("cannot create the install folder", error);
        }

        using var service = ServiceConfigurator.Open(create: true, out error);
        if (service is null)
        {
            return Fail("cannot open the Service Control Manager", error);
        }

        if (!service.StopAndWait())
        {
            return Fail("the running service did not stop within 5 s", Win32Error.Timeout);
        }

        var target = HelperProtocol.InstallPath(programFiles);
        if (!(SelfImage.TryIdentify(target, out var installed) && SelfImage.SameFile(installed, selfIdentity)))
        {
            if (InstallFolder.WriteBinary(target, image, out error) != StepResult.Ok)
            {
                return Fail(
                    error is Win32Error.AccessDenied or Win32Error.SharingViolation
                        ? "the installed helper is in use by another program; close it and try again"
                        : "cannot write the helper into Program Files",
                    error);
            }
        }

        var users = MultiString.AllowedUsers([.. HelperRegistry.ReadAllowedUsers(), userSid]);
        if (!users.Contains(userSid, StringComparer.OrdinalIgnoreCase))
        {
            return Refuse($"at most {MultiString.MaxAllowedUsers} users can be allowed");
        }

        error = HelperRegistry.WriteInstall(users);
        if (error != 0)
        {
            return Fail("cannot write the registry key", error);
        }

        error = service.Configure(target, users);
        if (error != 0)
        {
            return Fail("cannot create or configure the service", error);
        }

        error = ServiceIdentity.Sid() is { } serviceSid ? SessionSecurity.Apply(serviceSid) : Win32Error.InvalidParameter;
        if (error != 0)
        {
            return Fail("cannot set the security of the trace session", error);
        }

        Output.Line($"installed {HelperProtocol.BundledVersion} for {users.Count} user(s) ({how})");
        return HelperExitCode.Ok;
    }

    public static HelperExitCode Remove(string userSid)
    {
        if (!Tokens.IsElevatedAdministrator())
        {
            return Refuse("remove needs an elevated administrator token");
        }

        var remaining = HelperRegistry.ReadAllowedUsers();
        remaining.RemoveAll(user => string.Equals(user, userSid, StringComparison.OrdinalIgnoreCase));

        using var service = ServiceConfigurator.Open(create: false, out var error);
        if (service is null)
        {
            return Fail("cannot open the Service Control Manager", error);
        }

        if (remaining.Count > 0)
        {
            error = HelperRegistry.WriteAllowedUsers(remaining);
            if (error == 0 && service.Exists)
            {
                error = service.SetAccess(remaining);
            }

            if (error != 0)
            {
                return Fail("cannot update the allowed users", error);
            }

            // The pipe DACL is built at service start: stop it so the removed user loses access now.
            if (!service.StopAndWait())
            {
                return Fail("the allowed users were updated, but the running service did not stop within 5 s; the removed user keeps pipe access until it stops", Win32Error.Timeout);
            }

            Output.Line($"removed the user; {remaining.Count} user(s) remain");
            return HelperExitCode.Ok;
        }

        var stopped = service.StopAndWait();
        TraceSession.StopLeftover();
        error = service.Delete();
        if (error != 0)
        {
            return Fail("cannot delete the service", error);
        }

        error = HelperRegistry.Delete();
        if (error != 0)
        {
            return Fail("cannot delete the registry key", error);
        }

        error = SessionSecurity.Remove();
        if (error != 0)
        {
            return Fail("cannot remove the security of the trace session", error);
        }

        var programFiles = HelperRegistry.ProgramFilesDirectory();
        var deferred = false;
        if (programFiles is not null && InstallFolder.Delete(programFiles, out deferred) == StepResult.Refused)
        {
            return Refuse("the install folder is redirected; it was left in place");
        }

        Output.Line(deferred || !stopped ? "removed; files still in use are deleted at the next restart" : "removed");
        return HelperExitCode.Ok;
    }

    private static HelperExitCode Refuse(string message)
    {
        Output.Error("refused: " + message);
        return HelperExitCode.Refused;
    }

    private static HelperExitCode Fail(string message, uint error)
    {
        Output.Error(error == 0 ? "failed: " + message : $"failed: {message} (error {error})");
        return HelperExitCode.Failed;
    }
}
