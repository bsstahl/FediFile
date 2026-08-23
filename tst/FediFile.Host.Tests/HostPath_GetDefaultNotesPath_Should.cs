#pragma warning disable CA1707
using Xunit;

namespace FediFile.Host.Tests;

public sealed class HostPath_GetDefaultNotesPath_Should
{
    [Theory]
    [InlineData("@alice@example.social")]
    [InlineData("alice@example.social")]
    public void IncludeAtPrefixInNotesPath_WhenHandleIsValid(string actorHandle)
    {
        var path = HostPath.GetDefaultNotesPath(actorHandle);

        Assert.Equal(@"\@alice@example.social\Notes", path);
    }
}
#pragma warning restore CA1707
