using FediFile.Store;

namespace FediFile.WinFsp;

public sealed record FileSystemEntry(
    string Name,
    bool IsDirectory,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string? ContentType);

public interface IFediFileSystem
{
    ValueTask<FileSystemEntry?> GetEntryAsync(string path, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<FileSystemEntry>> ReadDirectoryAsync(string path, CancellationToken cancellationToken);
    ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);
    ValueTask<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken);
    ValueTask DeleteAsync(string path, CancellationToken cancellationToken);
    ValueTask RenameAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);
}

public sealed class FediFileSystem : IFediFileSystem
{
    private readonly IFediStore _store;
    private readonly FediMutationContext _mutationContext;

    public FediFileSystem(IFediStore store, FediMutationContext mutationContext)
    {
        _store = store;
        _mutationContext = mutationContext;
    }

    public async ValueTask<FileSystemEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
    {
        var node = await _store.TryGetNodeByPathAsync(new FediPath(path), cancellationToken).ConfigureAwait(false);
        return node is null
            ? null
            : new FileSystemEntry(node.Name, node.IsDirectory, node.Size ?? 0, node.LastModifiedUtc, node.ContentType);
    }

    public async ValueTask<IReadOnlyList<FileSystemEntry>> ReadDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        var children = await _store.ListDirectoryAsync(new FediPath(path), cancellationToken).ConfigureAwait(false);
        return children
            .Select(child => new FileSystemEntry(child.Name, child.IsDirectory, child.Size ?? 0, child.LastModifiedUtc, child.ContentType))
            .ToArray();
    }

    public async ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        var content = await _store.OpenReadAsync(new FediPath(path), cancellationToken).ConfigureAwait(false);
        return content.Stream;
    }

    public ValueTask<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
    {
#pragma warning disable CA2000
        return ValueTask.FromResult<Stream>(new BufferedNoteWriteStream(path, _store, _mutationContext, cancellationToken));
#pragma warning restore CA2000
    }

    public ValueTask DeleteAsync(string path, CancellationToken cancellationToken) =>
        _store.DeleteAsync(new FediPath(path), _mutationContext, cancellationToken);

    public async ValueTask RenameAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await _store.RenameAsync(new FediPath(sourcePath), new FediPath(destinationPath), _mutationContext, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class BufferedNoteWriteStream : MemoryStream
{
    private readonly string _path;
    private readonly IFediStore _store;
    private readonly FediMutationContext _context;
    private readonly CancellationToken _cancellationToken;

    public BufferedNoteWriteStream(string path, IFediStore store, FediMutationContext context, CancellationToken cancellationToken)
    {
        _path = path;
        _store = store;
        _context = context;
        _cancellationToken = cancellationToken;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Position = 0;
            using var reader = new StreamReader(this, leaveOpen: true);
            var content = reader.ReadToEnd();
            _store.WriteNoteAsync(new FediPath(_path), content, _context, _cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        base.Dispose(disposing);
    }
}

public sealed class WinFspAdapter
{
    private readonly IFediFileSystem _fileSystem;

    public WinFspAdapter(IFediFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    // These signatures intentionally mirror the shape of WinFsp/Dokan callback entrypoints.
    public async Task<int> GetFileInfoAsync(string fileName, CancellationToken cancellationToken)
    {
        var entry = await _fileSystem.GetEntryAsync(fileName, cancellationToken).ConfigureAwait(false);
        return entry is null ? unchecked((int)0xC0000034) : 0;
    }

    public async Task<IReadOnlyList<FileSystemEntry>> ReadDirectoryAsync(string fileName, CancellationToken cancellationToken) =>
        await _fileSystem.ReadDirectoryAsync(fileName, cancellationToken).ConfigureAwait(false);

    public async Task<Stream> CreateFileAsync(string fileName, FileAccess access, CancellationToken cancellationToken)
    {
        return access.HasFlag(FileAccess.Write)
            ? await _fileSystem.OpenWriteAsync(fileName, cancellationToken).ConfigureAwait(false)
            : await _fileSystem.OpenReadAsync(fileName, cancellationToken).ConfigureAwait(false);
    }

    public static Task CleanupAsync(string fileName, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DeleteFileAsync(string fileName, CancellationToken cancellationToken) =>
        _fileSystem.DeleteAsync(fileName, cancellationToken);

    public ValueTask RenameAsync(string oldName, string newName, CancellationToken cancellationToken) =>
        _fileSystem.RenameAsync(oldName, newName, cancellationToken);
}
