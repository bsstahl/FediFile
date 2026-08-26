#pragma warning disable CA1707
using FediFile.Store;
using FediFile.WinFsp;
using NSubstitute;
using System.Security.AccessControl;
using Xunit;

namespace FediFile.WinFsp.Tests;

public sealed class WinFspReadOnlyFileSystem_GetSecurityByName_Should
{
    [Fact]
    public void ReturnAttributesAndSecurityDescriptor_WhenPathExists()
    {
        var store = Substitute.For<IFediStore>();
        var node = new FediNode(
            "notes",
            "Notes",
            FediNodeKind.Collection,
            new FediPath(@"\@alice@example.social\Notes"),
            null,
            DateTimeOffset.UtcNow,
            null,
            true,
            "actor-1",
            "@alice@example.social");
    #pragma warning disable CA2012
        store.TryGetNodeByPathAsync(node.Path, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<FediNode?>(node));
    #pragma warning restore CA2012
        var target = new WinFspReadOnlyFileSystem(store);
        byte[] securityDescriptor = [];

        var result = target.GetSecurityByName(node.Path.FullPath, out var attributes, ref securityDescriptor);

        Assert.Equal(0, result);
        Assert.Equal(0x10u, attributes);
        Assert.NotNull(securityDescriptor);
        var descriptor = new RawSecurityDescriptor(securityDescriptor, 0);
        Assert.Equal("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;WD)", descriptor.GetSddlForm(AccessControlSections.All));
    }
}
#pragma warning restore CA1707
