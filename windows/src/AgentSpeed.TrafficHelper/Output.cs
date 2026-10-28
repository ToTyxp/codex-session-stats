using System.Text;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper;

/// <summary>Plain UTF-8 lines to stdout/stderr through WriteFile (the console stack is trimmed from the Windows image); silent without a console.</summary>
internal static unsafe class Output
{
    public static void Line(string text) => Write(Kernel32.StdOutput, text + "\n");

    public static void Error(string text) => Write(Kernel32.StdError, text + "\n");

    private static void Write(int which, string text)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.Write(text);
            return;
        }

        var handle = Kernel32.GetStdHandle(which);
        if (handle == 0 || handle == Kernel32.InvalidHandle)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* data = bytes)
        {
            uint written;
            Kernel32.WriteFile(handle, data, (uint)bytes.Length, &written, null);
        }
    }
}
