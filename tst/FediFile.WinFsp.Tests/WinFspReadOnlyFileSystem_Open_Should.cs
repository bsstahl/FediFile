#pragma warning disable CA1707
using System.Text;
using FediFile.Store;
using FediFile.WinFsp;
using Fsp.Interop;
using NSubstitute;
using Xunit;

namespace FediFile.WinFsp.Tests;

public sealed class WinFspReadOnlyFileSystem_Open_Should
{
    private readonly IFediStore _store;
    private readonly WinFspReadOnlyFileSystem target;

    public WinFspReadOnlyFileSystem_Open_Should()
    {
        _store = Substitute.For<IFediStore>();
        target = new WinFspReadOnlyFileSystem(_store);
    }

    [Fact]
    public void ReturnSuccessAndFileInfo_WhenNoteExists()
    {
        var node = new FediNode(
            "note-1",
            "note.html",
            FediNodeKind.Note,
            new FediPath(@"\@alice@example.social\Notes\note.html"),
            Encoding.UTF8.GetByteCount("hello"),
            DateTimeOffset.UtcNow,
            "text/html",
            false,
            "actor-1",
            "@alice@example.social");
#pragma warning disable CA2012
        _store.TryGetNodeByPathAsync(Arg.Any<FediPath>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<FediNode?>(node));
#pragma warning restore CA2012

        var result = target.Open(node.Path.FullPath, 0, 0, out var fileNode, out _, out var fileInfo, out _);

        Assert.Equal(0, result);
        Assert.Same(node, fileNode);
        Assert.Equal((ulong)node.Size!.Value, fileInfo.FileSize);
    }
}
#pragma warning restore CA1707
