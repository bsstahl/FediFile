#pragma warning disable CA1707
using FediFile.Store;
using FediFile.WinFsp;
using NSubstitute;
using Xunit;

namespace FediFile.WinFsp.Tests;

public sealed class WinFspMountHost_Mount_Should
{
    [Fact]
    public void RejectBlankMountPoint()
    {
        using var target = new WinFspMountHost(Substitute.For<IFediStore>());

        Assert.Throws<ArgumentException>(() => target.Mount(" "));
    }
}
#pragma warning restore CA1707
