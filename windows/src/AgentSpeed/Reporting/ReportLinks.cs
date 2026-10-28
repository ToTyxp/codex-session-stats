using System.Diagnostics;
using System.Windows;
using AgentSpeed.App;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Reporting;

/// <summary>Links from a service (REPORTING 2.3): only <c>https</c>, opened in the user's browser only when clicked.</summary>
internal static class ReportLinks
{
    public static void Open(string? link)
    {
        if (ReportUrls.HttpsLink(link) is not { } valid || !Uri.TryCreate(valid, UriKind.Absolute, out var uri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Live reporting: a service link could not be opened.");
        }
    }

    /// <summary>Copies text to the clipboard, retrying once when another program holds it.</summary>
    public static void Copy(string text)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(40);
            }
        }
    }
}
