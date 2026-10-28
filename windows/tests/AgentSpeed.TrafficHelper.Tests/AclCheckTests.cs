using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class AclCheckTests
{
    private const byte Allowed = 0;
    private const byte Denied = 1;
    private const byte AllowedObject = 5;
    private const byte InheritOnlyContainerObject = 0x08 | 0x01 | 0x02;
    private const uint ReadExecute = 0x001200A9;
    private const uint Modify = 0x001301BF;
    private const uint GenericAll = 0x10000000;
    private const uint GenericReadExecute = 0xA0000000;
    private const string Users = "S-1-5-32-545";
    private const string AllPackages = "S-1-15-2-1";
    private const string CreatorGroup = "S-1-3-1";

    /// <summary>The default ACL of <c>C:\Program Files</c> on Windows 11, which a new subfolder inherits.</summary>
    private static byte[][] ProgramFiles() =>
    [
        Sids.Ace(Allowed, 0, 0x001F01FF, SecurityText.TrustedInstallerSid),
        Sids.Ace(Allowed, 0x0A, GenericAll, SecurityText.TrustedInstallerSid),
        Sids.Ace(Allowed, 0, Modify, SecurityText.SystemSid),
        Sids.Ace(Allowed, InheritOnlyContainerObject, GenericAll, SecurityText.SystemSid),
        Sids.Ace(Allowed, 0, Modify, SecurityText.AdministratorsSid),
        Sids.Ace(Allowed, InheritOnlyContainerObject, GenericAll, SecurityText.AdministratorsSid),
        Sids.Ace(Allowed, 0, ReadExecute, Users),
        Sids.Ace(Allowed, InheritOnlyContainerObject, GenericReadExecute, Users),
        Sids.Ace(Allowed, InheritOnlyContainerObject, GenericAll, SecurityText.CreatorOwnerSid),
        Sids.Ace(Allowed, 0, ReadExecute, AllPackages),
        Sids.Ace(Allowed, InheritOnlyContainerObject, GenericReadExecute, AllPackages),
    ];

    [Fact]
    public void DefaultProgramFilesAclIsSafe() => Assert.True(AclCheck.IsAdminOnlyWritable(Sids.Acl(ProgramFiles())));

    [Fact]
    public void UsersWithWriteAccessAreUnsafe() =>
        Assert.False(AclCheck.IsAdminOnlyWritable(Sids.Acl([.. ProgramFiles(), Sids.Ace(Allowed, 0, 0x2, Users)])));

    [Fact]
    public void InheritOnlyWriteForUsersIsUnsafe() =>
        Assert.False(AclCheck.IsAdminOnlyWritable(Sids.Acl([.. ProgramFiles(), Sids.Ace(Allowed, InheritOnlyContainerObject, 0x40000000, Users)])));

    [Fact]
    public void CreatorGroupIsNotTrusted() =>
        Assert.False(AclCheck.IsAdminOnlyWritable(Sids.Acl([.. ProgramFiles(), Sids.Ace(Allowed, InheritOnlyContainerObject, GenericAll, CreatorGroup)])));

    [Theory]
    [InlineData(0x00010000u)]
    [InlineData(0x00040000u)]
    [InlineData(0x00080000u)]
    [InlineData(0x00000040u)]
    [InlineData(0x02000000u)]
    public void DeleteDaclOwnerDeleteChildAndMaximumAllowedCountAsWrite(uint mask) =>
        Assert.False(AclCheck.IsAdminOnlyWritable(Sids.Acl(Sids.Ace(Allowed, 0, mask, "S-1-1-0"))));

    [Fact]
    public void DenyAcesAreFine() =>
        Assert.True(AclCheck.IsAdminOnlyWritable(Sids.Acl([Sids.Ace(Denied, 0, GenericAll, "S-1-1-0"), .. ProgramFiles()])));

    [Fact]
    public void UnknownAllowTypeWithWriteRightsFailsClosed() =>
        Assert.False(AclCheck.IsAdminOnlyWritable(Sids.Acl([.. ProgramFiles(), Sids.Ace(AllowedObject, 0, 0x2, SecurityText.AdministratorsSid)])));

    [Fact]
    public void MalformedAclsAreUnsafe()
    {
        var acl = Sids.Acl(ProgramFiles());

        Assert.False(AclCheck.IsAdminOnlyWritable(acl.AsSpan(0, 7)));
        var truncated = (byte[])acl.Clone();
        truncated[4] = 200;
        Assert.False(AclCheck.IsAdminOnlyWritable(truncated));
        var badAce = (byte[])acl.Clone();
        badAce[8 + 2] = 4;
        badAce[8 + 3] = 0;
        Assert.False(AclCheck.IsAdminOnlyWritable(badAce));
    }

    [Theory]
    [InlineData(SecurityText.AdministratorsSid, true)]
    [InlineData(SecurityText.TrustedInstallerSid, true)]
    [InlineData(SecurityText.SystemSid, true)]
    [InlineData("S-1-5-21-1-2-3-1001", false)]
    [InlineData(Users, false)]
    [InlineData(null, false)]
    public void OnlyAdministratorsTrustedInstallerOrSystemMayOwnTheFolder(string? owner, bool trusted) =>
        Assert.Equal(trusted, AclCheck.IsTrustedOwner(owner));
}
