#pragma warning disable CA1707
using Xunit;

namespace FediFile.Host.Tests;

public sealed class HostPath_GetDefaultFollowingPath_Should
{
    [Theory]
    [InlineData("@alice@example.social")]
    [InlineData("alice@example.social")]
    public void IncludeAtPrefixInFollowingPath_WhenHandleIsValid(string actorHandle)
    {
        var path = HostPath.GetDefaultFollowingPath(actorHandle);

        Assert.Equal(@"\@alice@example.social\Following", path);
    }
}
#pragma warning restore CA1707
