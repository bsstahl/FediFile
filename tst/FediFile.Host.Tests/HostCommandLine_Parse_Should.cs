#pragma warning disable CA1707
using FediFile.Host;
using Xunit;

namespace FediFile.Host.Tests;

public sealed class HostCommandLine_Parse_Should
{
    [Fact]
    public void ParseLegacyMountArguments()
    {
        var options = HostCommandLine.Parse(["F:", "@alice@example.social"]);

        Assert.Equal("MOUNT", options.Command);
        Assert.Equal("F:", options.MountPoint);
        Assert.Equal("@alice@example.social", options.ActorHandle);
    }

    [Fact]
    public void ParseExplicitMountArguments()
    {
        var options = HostCommandLine.Parse(["mount", "F:", "@alice@example.social"]);

        Assert.Equal("MOUNT", options.Command);
        Assert.Equal("F:", options.MountPoint);
        Assert.Equal("@alice@example.social", options.ActorHandle);
    }
}
#pragma warning restore CA1707
