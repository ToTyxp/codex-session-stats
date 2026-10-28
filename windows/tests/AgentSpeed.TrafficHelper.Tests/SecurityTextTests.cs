using System.Security.Cryptography;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class SecurityTextTests
{
    private const string User = "S-1-5-21-1004336348-1177238915-682003330-1001";
    private const string Other = "S-1-5-21-1004336348-1177238915-682003330-1002";

    [Fact]
    public void PipeDescriptorIsOwnedByTheServiceAndProtected()
    {
        var sddl = SecurityText.PipeSddl("S-1-5-80-1-2-3-4-5", [User, Other]);

        Assert.Equal(
            "O:S-1-5-80-1-2-3-4-5D:P(D;;GA;;;NU)(A;;GA;;;SY)(A;;GA;;;S-1-5-80-1-2-3-4-5)" +
            $"(A;;0x12008b;;;{User})(A;;0x12008b;;;{Other})S:(ML;;NWNR;;;ME)",
            sddl);
    }

    [Fact]
    public void PipeClientRightsAreReadAndWriteDataWithoutCreateInstance()
    {
        const uint fileGenericRead = 0x00120089;
        const uint fileWriteData = 0x2;
        const uint fileCreatePipeInstance = 0x4;

        Assert.Equal(fileGenericRead | fileWriteData, SecurityText.PipeClientAccess);
        Assert.Equal(0u, SecurityText.PipeClientAccess & fileCreatePipeInstance);
    }

    [Fact]
    public void ServiceDescriptorGrantsUsersOnlyStatusStartStopInterrogateAndReadControl()
    {
        var sddl = SecurityText.ServiceSddl([User]);

        Assert.Equal($"D:(A;;0xf01ff;;;SY)(A;;0xf01ff;;;BA)(A;;0x200b4;;;{User})(A;;0x4;;;AU)", sddl);
        const uint queryStatus = 0x4, start = 0x10, stop = 0x20, interrogate = 0x80, readControl = 0x20000;
        Assert.Equal(queryStatus | start | stop | interrogate | readControl, SecurityText.ServiceUserAccess);
        const uint changeConfig = 0x2, writeDac = 0x40000, writeOwner = 0x80000;
        Assert.Equal(0u, SecurityText.ServiceUserAccess & (changeConfig | writeDac | writeOwner));
    }

    [Fact]
    public void ServiceSidMatchesTheScmAlgorithm()
    {
        // NT SERVICE\TrustedInstaller is the well-known service SID of the "TrustedInstaller" service.
        var hash = SHA1.HashData(SecurityText.ServiceNameBytes("TrustedInstaller"));

        Assert.Equal(SecurityText.TrustedInstallerSid, SecurityText.ServiceSidFromHash(hash));

        // `sc showsid AgentSpeedTraffic` on the test machine.
        Assert.Equal(
            "S-1-5-80-2420399511-3182910596-2363154076-518365163-244096683",
            SecurityText.ServiceSidFromHash(SHA1.HashData(SecurityText.ServiceNameBytes())));
    }

    [Fact]
    public void ProcessDaclKeepsItsAcesAndAddsQueryLimitedForEachUser()
    {
        var sddl = SecurityText.ProcessQuerySddl("D:(A;;0x1fffff;;;S-1-5-5-0-259730773)(A;;0x1400;;;BA)", [User]);

        Assert.Equal($"D:(A;;0x1fffff;;;S-1-5-5-0-259730773)(A;;0x1400;;;BA)(A;;0x1000;;;{User})", sddl);
    }

    [Fact]
    public void BinarySidRoundTrip()
    {
        Assert.Equal(User, SecurityText.SidToString(Sids.Binary(User)));
        Assert.Equal("S-1-5-18", SecurityText.SidToString(Sids.Binary("S-1-5-18")));
        Assert.Null(SecurityText.SidToString(new byte[] { 2, 0, 0, 0, 0, 0, 0, 5 }));
        Assert.Null(SecurityText.SidToString(new byte[] { 1, 3, 0, 0, 0, 0, 0, 5, 1, 0, 0, 0 }));
    }

    [Fact]
    public void Sha256HexParsing()
    {
        Assert.True(SecurityText.TryParseSha256(new string('a', 64), out var hash));
        Assert.All(hash, b => Assert.Equal(0xAA, b));
        Assert.False(SecurityText.TryParseSha256(new string('a', 63), out _));
        Assert.False(SecurityText.TryParseSha256(new string('a', 62) + "x1", out _));
        Assert.False(SecurityText.TryParseSha256(null, out _));
        Assert.Equal(new string('a', 64), SecurityText.ToHex(hash));
    }

    [Fact]
    public void MultiStringRoundTripKeepsOnlyValidUniqueUsers()
    {
        var data = MultiString.Format([User, "S-1-1-0", User.ToLowerInvariant(), Other, ""]);
        var parsed = MultiString.Parse(data);

        Assert.Equal([User, "S-1-1-0", User.ToLowerInvariant(), Other], parsed);
        Assert.Equal([User, Other], MultiString.AllowedUsers(parsed));
        Assert.Empty(MultiString.Parse([]));
        Assert.Equal(["x"], MultiString.Parse("x"u8.ToArray().SelectMany(b => new byte[] { b, 0 }).ToArray()));
    }

    [Fact]
    public void AllowedUsersAreCapped()
    {
        var many = Enumerable.Range(1000, 100).Select(rid => $"S-1-5-21-1-2-3-{rid}");

        Assert.Equal(MultiString.MaxAllowedUsers, MultiString.AllowedUsers(many).Count);
    }
}
