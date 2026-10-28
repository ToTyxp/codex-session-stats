using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Install;

/// <summary>
/// This process's own executable (WINDOWS-SERVICE 5.2): read into memory through a handle that shares neither write nor
/// delete, so the bytes that are hashed are exactly the bytes later written to Program Files.
/// </summary>
internal static unsafe class SelfImage
{
    private const long MaxSize = 64L * 1024 * 1024;

    public static string? Path()
    {
        var buffer = stackalloc char[1024];
        var length = Kernel32.GetModuleFileNameW(0, buffer, 1024);
        return length is 0 or >= 1024 ? null : new string(buffer, 0, (int)length);
    }

    /// <summary>The image bytes and the file's identity (volume serial, file index), or null with a Win32 error.</summary>
    public static byte[]? Read(string path, out FileInformation identity, out uint error)
    {
        identity = default;
        var file = Kernel32.CreateFileW(path, Kernel32.GenericRead, Kernel32.FileShareRead, null, Kernel32.OpenExisting, Kernel32.FileAttributeNormal, 0);
        if (file == Kernel32.InvalidHandle)
        {
            error = Kernel32.LastError;
            return null;
        }

        try
        {
            long size;
            FileInformation information;
            if (Kernel32.GetFileSizeEx(file, &size) == 0 || Kernel32.GetFileInformationByHandle(file, &information) == 0)
            {
                error = Kernel32.LastError;
                return null;
            }

            if (size is <= 0 or > MaxSize)
            {
                error = Win32Error.InvalidParameter;
                return null;
            }

            var bytes = new byte[size];
            var total = 0;
            fixed (byte* data = bytes)
            {
                while (total < bytes.Length)
                {
                    uint read;
                    if (Kernel32.ReadFile(file, data + total, (uint)(bytes.Length - total), &read, null) == 0 || read == 0)
                    {
                        error = read == 0 ? Win32Error.InvalidParameter : Kernel32.LastError;
                        return null;
                    }

                    total += (int)read;
                }
            }

            identity = information;
            error = 0;
            return bytes;
        }
        finally
        {
            Kernel32.CloseHandle(file);
        }
    }

    /// <summary>The identity of an existing file, opened without following a final reparse point; false when absent.</summary>
    public static bool TryIdentify(string path, out FileInformation identity)
    {
        identity = default;
        var file = Kernel32.CreateFileW(path, 0, 0x7, null, Kernel32.OpenExisting, 0x00200000, 0);
        if (file == Kernel32.InvalidHandle)
        {
            return false;
        }

        try
        {
            FileInformation information;
            if (Kernel32.GetFileInformationByHandle(file, &information) == 0)
            {
                return false;
            }

            identity = information;
            return true;
        }
        finally
        {
            Kernel32.CloseHandle(file);
        }
    }

    public static bool SameFile(in FileInformation a, in FileInformation b) =>
        a.VolumeSerialNumber == b.VolumeSerialNumber && a.FileIndexHigh == b.FileIndexHigh && a.FileIndexLow == b.FileIndexLow;
}
