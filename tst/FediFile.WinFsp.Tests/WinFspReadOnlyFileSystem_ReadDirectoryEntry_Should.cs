#pragma warning disable CA1707
using FediFile.Store;
using FediFile.WinFsp;
using NSubstitute;
using Xunit;

namespace FediFile.WinFsp.Tests;

public sealed class WinFspReadOnlyFileSystem_ReadDirectoryEntry_Should
{
    [Fact]
    public void ReturnChildEntry_WhenDirectoryHasChildren()
    {
        var store = Substitute.For<IFediStore>();
        var directory = new FediNode(
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
        var child = new FediNode(
            "note-1",
            "note.html",
            FediNodeKind.Note,
            new FediPath(@"\@alice@example.social\Notes\note.html"),
            5,
            DateTimeOffset.UtcNow,
            "text/html",
            false,
            "actor-1",
            "@alice@example.social");
    #pragma warning disable CA2012
        store.ListDirectoryAsync(directory.Path, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<FediNode>>([child]));
    #pragma warning restore CA2012
        var target = new WinFspReadOnlyFileSystem(store);
        object context = null!;

        var hasEntry = target.ReadDirectoryEntry(directory, directory, "*", null!, ref context, out var fileName, out var fileInfo);

        Assert.True(hasEntry);
        Assert.Equal("note.html", fileName);
        Assert.Equal((ulong)5, fileInfo.FileSize);
    }
}
#pragma warning restore CA1707
