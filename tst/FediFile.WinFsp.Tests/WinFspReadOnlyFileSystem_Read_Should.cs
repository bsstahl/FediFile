#pragma warning disable CA1707
using System.Runtime.InteropServices;
using FediFile.Store;
using FediFile.WinFsp;
using NSubstitute;
using Xunit;

namespace FediFile.WinFsp.Tests;

public sealed class WinFspReadOnlyFileSystem_Read_Should
{
    [Fact]
    public void CopyRequestedBytes_WhenNoteIsReadable()
    {
        var store = Substitute.For<IFediStore>();
        var node = new FediNode(
            "note-1",
            "note.html",
            FediNodeKind.Note,
            new FediPath(@"\@alice@example.social\Notes\note.html"),
            11,
            DateTimeOffset.UtcNow,
            "text/html",
            false,
            "actor-1",
            "@alice@example.social");
    #pragma warning disable CA2012
        store.OpenReadAsync(node.Path, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new FediContent("text/html", new MemoryStream("hello world"u8.ToArray()), 11)));
    #pragma warning restore CA2012
        var target = new WinFspReadOnlyFileSystem(store);
        var buffer = Marshal.AllocHGlobal(5);

        try
        {
            var result = target.Read(node, node, buffer, 6, 5, out var bytesTransferred);
            var bytes = new byte[5];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);

            Assert.Equal(0, result);
            Assert.Equal((uint)5, bytesTransferred);
            Assert.Equal("world", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
#pragma warning restore CA1707
