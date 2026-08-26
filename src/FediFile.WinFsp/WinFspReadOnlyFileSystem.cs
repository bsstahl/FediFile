using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using FediFile.Store;
using Fsp;
using WinFspFileInfo = Fsp.Interop.FileInfo;
using WinFspVolumeInfo = Fsp.Interop.VolumeInfo;

namespace FediFile.WinFsp;

public sealed class WinFspReadOnlyFileSystem(IFediStore store) : FileSystemBase
{
    private const int StatusEndOfFile = unchecked((int)0xC0000011);
    private const int StatusObjectNameNotFound = unchecked((int)0xC0000034);
    private const int StatusSuccess = 0;
    private static readonly byte[] ReadOnlySecurityDescriptor = CreateReadOnlySecurityDescriptor();

    private readonly IFediStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public override int GetSecurityByName(
        string FileName,
        out uint FileAttributes,
        ref byte[] SecurityDescriptor)
    {
        try
        {
            var node = GetNode(FileName);
            FileAttributes = ToFileInfo(node).FileAttributes;
            SecurityDescriptor = [.. ReadOnlySecurityDescriptor];
            return StatusSuccess;
        }
        catch (FileNotFoundException)
        {
            FileAttributes = 0;
            SecurityDescriptor = [];
            return StatusObjectNameNotFound;
        }
    }

    public override int GetVolumeInfo(out WinFspVolumeInfo VolumeInfo)
    {
        VolumeInfo = new WinFspVolumeInfo
        {
            TotalSize = 1024UL * 1024 * 1024,
            FreeSize = 1024UL * 1024 * 1024
        };
        VolumeInfo.SetVolumeLabel("FediFile");
        return StatusSuccess;
    }

    public override int Open(
        string FileName,
        uint CreateOptions,
        uint GrantedAccess,
        out object FileNode,
        out object FileDesc,
        out WinFspFileInfo FileInfo,
        out string NormalizedName)
    {
        try
        {
            var node = GetNode(FileName);
            FileNode = node;
            FileDesc = node;
            FileInfo = ToFileInfo(node);
            NormalizedName = node.Path.FullPath;
            return StatusSuccess;
        }
        catch (FileNotFoundException)
        {
            FileNode = null!;
            FileDesc = null!;
            FileInfo = default;
            NormalizedName = string.Empty;
            return StatusObjectNameNotFound;
        }
    }

    public override int GetFileInfo(object FileNode, object FileDesc, out WinFspFileInfo FileInfo)
    {
        if (FileNode is not FediNode node)
        {
            FileInfo = default;
            return StatusObjectNameNotFound;
        }

        FileInfo = ToFileInfo(node);
        return StatusSuccess;
    }

    public override int Read(
        object FileNode,
        object FileDesc,
        IntPtr Buffer,
        ulong Offset,
        uint Length,
        out uint BytesTransferred)
    {
        BytesTransferred = 0;
        if (FileNode is not FediNode node || node.IsDirectory)
        {
            return StatusObjectNameNotFound;
        }

        var content = _store.OpenReadAsync(node.Path, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        using var stream = content.Stream;

        if (Offset > long.MaxValue || !stream.CanSeek && Offset > int.MaxValue)
        {
            return StatusEndOfFile;
        }

        if (stream.CanSeek)
        {
            stream.Seek((long)Offset, SeekOrigin.Begin);
        }
        else
        {
            stream.Position = 0;
            stream.CopyTo(Stream.Null, (int)Offset);
        }

        var buffer = new byte[(int)Math.Min(Length, int.MaxValue)];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read > 0)
        {
            Marshal.Copy(buffer, 0, Buffer, read);
        }

        BytesTransferred = (uint)read;
        return read == 0 ? StatusEndOfFile : StatusSuccess;
    }

    public override bool ReadDirectoryEntry(
        object FileNode,
        object FileDesc,
        string Pattern,
        string Marker,
        ref object Context,
        out string FileName,
        out WinFspFileInfo FileInfo)
    {
        FileName = string.Empty;
        FileInfo = default;

        if (FileNode is not FediNode node || !node.IsDirectory)
        {
            return false;
        }

        var entries = _store.ListDirectoryAsync(node.Path, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult()
            .Where(entry => string.IsNullOrEmpty(Marker) || string.Compare(entry.Name, Marker, StringComparison.OrdinalIgnoreCase) > 0)
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (Context is FediNode previous)
        {
            entries = entries.SkipWhile(entry => !string.Equals(entry.Name, previous.Name, StringComparison.OrdinalIgnoreCase)).Skip(1).ToArray();
        }

        if (entries.Length == 0)
        {
            return false;
        }

        var child = entries[0];
        Context = child;
        FileName = child.Name;
        FileInfo = ToFileInfo(child);
        return true;
    }

    private FediNode GetNode(string path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? FediPath.Root : new FediPath(path);
        return normalizedPath == FediPath.Root
            ? new FediNode("root", "", FediNodeKind.Actor, FediPath.Root, null, DateTimeOffset.UtcNow, null, true, null, null)
            : _store.TryGetNodeByPathAsync(normalizedPath, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult()
                ?? throw new FileNotFoundException(path);
    }

    private static WinFspFileInfo ToFileInfo(FediNode node)
    {
        var fileSize = (ulong)Math.Max(0, node.Size ?? 0);
        var fileAttributes = node.IsDirectory ? 0x10u : 0x80u;
        var fileTime = (ulong)node.LastModifiedUtc.UtcDateTime.ToFileTimeUtc();
        return new WinFspFileInfo
        {
            FileAttributes = fileAttributes,
            AllocationSize = fileSize,
            FileSize = fileSize,
            CreationTime = fileTime,
            LastAccessTime = fileTime,
            LastWriteTime = fileTime,
            ChangeTime = fileTime,
            HardLinks = 1
        };
    }

    private static byte[] CreateReadOnlySecurityDescriptor()
    {
        var descriptor = new RawSecurityDescriptor("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;WD)");
        var binaryForm = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binaryForm, 0);
        return binaryForm;
    }
}
