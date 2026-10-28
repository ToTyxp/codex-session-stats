using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Install;

/// <summary>The outcome of a folder or file step.</summary>
internal enum StepResult
{
    Ok,

    /// <summary>A safety check failed (insecure or redirected folder): exit 1.</summary>
    Refused,

    /// <summary>An operation failed: exit 2.</summary>
    Failed,
}

/// <summary>
/// <c>%ProgramFiles%\Agent Speed\Traffic Helper</c> (WINDOWS-SERVICE 5.2): created owned by Administrators with the
/// inherited Program Files ACL, or accepted only when it is a real directory (no reparse point) owned by Administrators,
/// TrustedInstaller or SYSTEM that no other principal can write. The binary is written as <c>.new</c>, flushed and renamed
/// over the target with REPLACE_EXISTING | WRITE_THROUGH.
/// </summary>
internal static unsafe class InstallFolder
{
    private const string AdminOwnedSddl = "O:BA";
    private const long ReplaceRetryMs = 3_000;

    public static string Parent(string programFiles) => Path.Combine(programFiles, "Agent Speed");

    public static StepResult Prepare(string programFiles, out uint error)
    {
        var result = PrepareDirectory(Parent(programFiles), out error);
        return result == StepResult.Ok ? PrepareDirectory(HelperProtocol.InstallFolder(programFiles), out error) : result;
    }

    public static StepResult WriteBinary(string target, ReadOnlySpan<byte> image, out uint error)
    {
        var temporary = target + ".new";
        if (Kernel32.DeleteFileW(temporary) == 0 && Kernel32.LastError is not (Win32Error.FileNotFound or Win32Error.PathNotFound))
        {
            error = Kernel32.LastError;
            return StepResult.Failed;
        }

        using var descriptor = SecurityDescriptor.FromSddl(AdminOwnedSddl, out error);
        if (descriptor is null)
        {
            return StepResult.Failed;
        }

        var attributes = new SecurityAttributes { Length = (uint)sizeof(SecurityAttributes), SecurityDescriptor = descriptor.Pointer };
        var file = Kernel32.CreateFileW(temporary, Kernel32.GenericWrite, 0, &attributes, Kernel32.CreateNew, Kernel32.FileAttributeNormal, 0);
        if (file == Kernel32.InvalidHandle)
        {
            error = Kernel32.LastError;
            return StepResult.Failed;
        }

        var written = true;
        fixed (byte* data = image)
        {
            var offset = 0;
            while (offset < image.Length)
            {
                uint count;
                if (Kernel32.WriteFile(file, data + offset, (uint)Math.Min(image.Length - offset, 1 << 20), &count, null) == 0 || count == 0)
                {
                    written = false;
                    break;
                }

                offset += (int)count;
            }
        }

        written = written && Kernel32.FlushFileBuffers(file) != 0;
        error = written ? 0 : Kernel32.LastError;
        Kernel32.CloseHandle(file);
        if (written)
        {
            error = Replace(temporary, target);
            if (error == 0)
            {
                return StepResult.Ok;
            }
        }

        Kernel32.DeleteFileW(temporary);
        return StepResult.Failed;
    }

    /// <summary>
    /// Renames <paramref name="source"/> over <paramref name="target"/>; while the target is still in use (an exiting
    /// image, a scanner, another program holding it open) it retries for up to 3 s, then gives up with the error.
    /// </summary>
    private static uint Replace(string source, string target)
    {
        var deadline = Environment.TickCount64 + ReplaceRetryMs;
        while (true)
        {
            if (Kernel32.MoveFileExW(source, target, Kernel32.MoveFileReplaceExisting | Kernel32.MoveFileWriteThrough) != 0)
            {
                return 0;
            }

            var error = Kernel32.LastError;
            if (error is not (Win32Error.AccessDenied or Win32Error.SharingViolation) || Environment.TickCount64 >= deadline)
            {
                return error;
            }

            Thread.Sleep(200);
        }
    }

    /// <summary>Deletes the binary and the folder (and <c>Agent Speed</c> when empty); in-use files go at the next reboot.</summary>
    public static StepResult Delete(string programFiles, out bool deferred)
    {
        deferred = false;
        var parent = Parent(programFiles);
        var parentAttributes = Kernel32.GetFileAttributesW(parent);
        if (parentAttributes == Kernel32.InvalidFileAttributes)
        {
            return StepResult.Ok;
        }

        if ((parentAttributes & Kernel32.FileAttributeReparsePoint) != 0)
        {
            // Never follow a redirected parent; leave it for an administrator.
            return StepResult.Refused;
        }

        var folder = HelperProtocol.InstallFolder(programFiles);
        var attributes = Kernel32.GetFileAttributesW(folder);
        if (attributes != Kernel32.InvalidFileAttributes)
        {
            if ((attributes & Kernel32.FileAttributeReparsePoint) == 0)
            {
                var binary = HelperProtocol.InstallPath(programFiles);
                deferred |= DeleteFile(binary);
                deferred |= DeleteFile(binary + ".new");
            }

            if (Kernel32.RemoveDirectoryW(folder) == 0)
            {
                deferred |= Kernel32.MoveFileExW(folder, null, Kernel32.MoveFileDelayUntilReboot) != 0;
            }
        }

        if (Kernel32.RemoveDirectoryW(parent) == 0 && deferred)
        {
            // Removed at boot after its child, and only if it is empty by then.
            Kernel32.MoveFileExW(parent, null, Kernel32.MoveFileDelayUntilReboot);
        }

        return StepResult.Ok;
    }

    private static bool DeleteFile(string path)
    {
        if (Kernel32.DeleteFileW(path) != 0 || Kernel32.LastError is Win32Error.FileNotFound or Win32Error.PathNotFound)
        {
            return false;
        }

        return Kernel32.MoveFileExW(path, null, Kernel32.MoveFileDelayUntilReboot) != 0;
    }

    private static StepResult PrepareDirectory(string path, out uint error)
    {
        error = 0;
        var attributes = Kernel32.GetFileAttributesW(path);
        if (attributes == Kernel32.InvalidFileAttributes)
        {
            using var descriptor = SecurityDescriptor.FromSddl(AdminOwnedSddl, out error);
            if (descriptor is null)
            {
                return StepResult.Failed;
            }

            var security = new SecurityAttributes { Length = (uint)sizeof(SecurityAttributes), SecurityDescriptor = descriptor.Pointer };
            if (Kernel32.CreateDirectoryW(path, &security) == 0)
            {
                error = Kernel32.LastError;
                return StepResult.Failed;
            }

            attributes = Kernel32.GetFileAttributesW(path);
        }

        if (attributes == Kernel32.InvalidFileAttributes || (attributes & Kernel32.FileAttributeDirectory) == 0
            || (attributes & Kernel32.FileAttributeReparsePoint) != 0)
        {
            error = Win32Error.AccessDenied;
            return StepResult.Refused;
        }

        if (!Tokens.TryGetFileSecurity(path, out var owner, out var dacl, out error))
        {
            return StepResult.Failed;
        }

        if (!AclCheck.IsTrustedOwner(owner) || dacl is null || !AclCheck.IsAdminOnlyWritable(dacl))
        {
            error = Win32Error.AccessDenied;
            return StepResult.Refused;
        }

        return StepResult.Ok;
    }
}
