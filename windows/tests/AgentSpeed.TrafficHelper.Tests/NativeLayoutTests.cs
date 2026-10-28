using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Counting;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Tests;

/// <summary>The 64-bit native layouts the helper relies on (x64 and arm64 share them).</summary>
public sealed unsafe class NativeLayoutTests
{
    [Fact]
    public void EtwStructuresHaveTheirWindowsSizes()
    {
        Assert.Equal(112, sizeof(EventRecord));
        Assert.Equal(448, sizeof(EventTraceLogfile));
        Assert.Equal(EventTraceProperties.TotalSize, sizeof(EventTraceProperties));
        Assert.Equal(48, sizeof(EnableTraceParameters));
        Assert.Equal(16, sizeof(EventFilterDescriptor));
    }

    [Fact]
    public void EtwFieldOffsets()
    {
        var record = default(EventRecord);
        var start = (byte*)&record;
        Assert.Equal(40, (byte*)&record.Id - start);
        Assert.Equal(86, (byte*)&record.UserDataLength - start);
        Assert.Equal(96, (byte*)&record.UserData - start);

        var properties = default(EventTraceProperties);
        Assert.Equal(24, (byte*)&properties.WnodeGuid - (byte*)&properties);
        Assert.Equal(116, (byte*)&properties.LoggerNameOffset - (byte*)&properties);

        var parameters = default(EnableTraceParameters);
        Assert.Equal(32, (byte*)&parameters.EnableFilterDesc - (byte*)&parameters);
        Assert.Equal(40, (byte*)&parameters.FilterDescCount - (byte*)&parameters);
    }

    [Fact]
    public void Win32StructuresHaveTheirWindowsSizes()
    {
        Assert.Equal(52, sizeof(FileInformation));
        Assert.Equal(32, sizeof(IoOverlapped));
        Assert.Equal(24, sizeof(SecurityAttributes));
        Assert.Equal(28, sizeof(Advapi32.ServiceStatus));
        Assert.Equal(36, sizeof(Advapi32.ServiceStatusProcess));
        Assert.Equal(16, sizeof(Advapi32.ServiceTableEntry));
        Assert.Equal(40, sizeof(Advapi32.ServiceFailureActions));
        Assert.Equal(8, sizeof(Advapi32.ScAction));
    }

    [Fact]
    public void SessionSecurityGrantsTheServiceOnlyWhatItUses()
    {
        const uint query = 0x1, createRealtime = 0x20, guidEnable = 0x80, accessRealtime = 0x400;

        Assert.Equal(query | createRealtime | guidEnable | accessRealtime, AgentSpeed.TrafficHelper.Etw.SessionSecurity.ServiceAccess);
        Assert.NotEqual(Guid.Empty, AgentSpeed.TrafficHelper.Etw.SessionSecurity.SessionGuid);
    }

    [Fact]
    public void HelperVersionMatchesTheProjectVersion()
    {
        var version = typeof(Program).Assembly.GetName().Version!;

        Assert.Equal(HelperProtocol.BundledVersion, new HelperVersion(version.Major, version.Minor, version.Build));
    }

    [Fact]
    public void LoopbackHelpers()
    {
        Assert.True(Loopback.IsIPv4([127, 1, 2, 3]));
        Assert.False(Loopback.IsIPv4([126, 1, 2, 3]));
        Assert.False(Loopback.IsIPv4([127, 1, 2]));
        Assert.False(Loopback.IsIPv6(new byte[15]));
        Assert.False(Loopback.IsIPv6(new byte[16]));
    }
}
