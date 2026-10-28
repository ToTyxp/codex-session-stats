using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class CommandLineTests
{
    private const string User = "S-1-5-21-1004336348-1177238915-682003330-1001";
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789ABCDEF";

    [Fact]
    public void NoArgumentsRunsTheService() => Assert.Equal(HelperVerb.Service, CommandLine.Parse([]).Verb);

    [Fact]
    public void InstallTakesUserAndHashInEitherOrder()
    {
        var first = CommandLine.Parse(["install", "--user", User, "--sha256", Hash]);
        var second = CommandLine.Parse(["install", "--sha256", Hash, "--user", User]);

        Assert.Equal(HelperVerb.Install, first.Verb);
        Assert.Equal(User, first.UserSid);
        Assert.Equal(32, first.Sha256!.Length);
        Assert.Equal(0x01, first.Sha256[0]);
        Assert.Equal(0xEF, first.Sha256[31]);
        Assert.Equal(first.UserSid, second.UserSid);
        Assert.Equal(first.Sha256, second.Sha256);
    }

    [Fact]
    public void RemoveTakesTheUser()
    {
        var command = CommandLine.Parse(["remove", "--user", User]);

        Assert.Equal(HelperVerb.Remove, command.Verb);
        Assert.Equal(User, command.UserSid);
    }

    public static TheoryData<string[]> Invalid() => new()
    {
        new[] { "install" },
        new[] { "install", "--user", User },
        new[] { "install", "--user", User, "--sha256" },
        new[] { "install", "--user", User, "--user", User },
        new[] { "install", "--sha256", Hash, "--sha256", Hash },
        new[] { "install", "--user", User, "--sha256", Hash, "extra" },
        new[] { "install", "--user", User, "--sha256", Hash[..63] },
        new[] { "install", "--user", User, "--sha256", Hash + "0" },
        new[] { "install", "--user", User, "--sha256", "g" + Hash[1..] },
        new[] { "install", "--user", "S-1-1-0", "--sha256", Hash },
        new[] { "INSTALL", "--user", User, "--sha256", Hash },
        new[] { "remove" },
        new[] { "remove", "--user" },
        new[] { "remove", "--user", User, "--force" },
        new[] { "remove", "--sid", User },
        new[] { "uninstall", "--user", User },
        new[] { "--help" },
        new[] { "" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AnythingElseIsUsage(string[] args) => Assert.Equal(HelperVerb.Usage, CommandLine.Parse(args).Verb);

    [Theory]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-1001", true)]
    [InlineData("s-1-5-21-1004336348-1177238915-682003330-500", true)]
    [InlineData("S-1-12-1-1234567-2345678-3456789-4567890", true)]
    [InlineData("S-1-1-0", false)]
    [InlineData("S-1-5-11", false)]
    [InlineData("S-1-5-18", false)]
    [InlineData("S-1-5-32-545", false)]
    [InlineData("S-1-5-32-544", false)]
    [InlineData("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", false)]
    [InlineData("S-1-5-21-1-2", false)]
    [InlineData("S-1-5-21-01004336348-1177238915-682003330-1001", false)]
    [InlineData("S-1-5-21-4294967296-1177238915-682003330-1001", false)]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-1001-", false)]
    [InlineData("S-1-5-21-1004336348--682003330-1001", false)]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-1001)(A;;GA;;;WD", false)]
    [InlineData("S-1-5-21-１２３-1177238915-682003330-1001", false)]
    [InlineData("", false)]
    public void OnlyCanonicalUserSidsAreAccepted(string text, bool valid)
    {
        Assert.Equal(valid, SidText.TryNormalizeUserSid(text, out var sid));
        if (valid)
        {
            Assert.Equal("S" + text[1..], sid);
        }
    }
}
