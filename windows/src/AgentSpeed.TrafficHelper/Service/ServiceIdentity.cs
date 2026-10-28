using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Service;

/// <summary>The service SID <c>NT SERVICE\AgentSpeedTraffic</c>, computed like the SCM does (SHA-1 of the upper-case name).</summary>
internal static class ServiceIdentity
{
    public static string? Sid()
    {
        Span<byte> hash = stackalloc byte[20];
        return SystemLibraries.Sha1(SecurityText.ServiceNameBytes(), hash) ? SecurityText.ServiceSidFromHash(hash) : null;
    }
}
