#pragma warning disable CA1707
using FediFile.Store;
using FediFile.WinFsp;
using NSubstitute;
using Xunit;

namespace FediFile.WinFsp.Tests;

public sealed class WinFspReadOnlyFileSystem_Write_Should
{
    [Fact]
    public void DenyCreate_WhenFilesystemIsReadOnly()
    {
        var target = new WinFspReadOnlyFileSystem(Substitute.For<IFediStore>());

        var result = target.Create(@"\new.html", 0, 0, 0, [], 0, out _, out _, out _, out _);

        Assert.Equal(unchecked((int)0xC0000010), result);
    }
}
#pragma warning restore CA1707
