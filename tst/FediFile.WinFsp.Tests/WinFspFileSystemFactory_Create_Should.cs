#pragma warning disable CA1707
using FediFile.Store;
using FediFile.WinFsp;
using Fsp;
using NSubstitute;
using Xunit;

namespace FediFile.WinFsp.Tests;

public sealed class WinFspFileSystemFactory_Create_Should
{
    [Fact]
    public void ReturnReadOnlyAdapter()
    {
        var target = new WinFspFileSystemFactory();

        var fileSystem = target.Create(Substitute.For<IFediStore>());

        Assert.IsType<WinFspReadOnlyFileSystem>(fileSystem);
    }
}
#pragma warning restore CA1707
