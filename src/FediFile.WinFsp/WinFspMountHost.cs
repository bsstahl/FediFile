using Fsp;
using FediFile.Store;

namespace FediFile.WinFsp;

public sealed class WinFspMountHost : IDisposable
{
    private readonly IFediStore _store;
    private readonly IWinFspFileSystemFactory _fileSystemFactory;
    private FileSystemHost? _host;

    public WinFspMountHost(IFediStore store, IWinFspFileSystemFactory? fileSystemFactory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _fileSystemFactory = fileSystemFactory ?? new WinFspFileSystemFactory();
    }

    public string? MountPoint => _host?.MountPoint();

    public void Mount(string mountPoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountPoint);

        if (_host is not null)
        {
            throw new InvalidOperationException("The filesystem is already mounted.");
        }

        var host = new FileSystemHost(_fileSystemFactory.Create(_store))
        {
            FileSystemName = "FediFile",
            CaseSensitiveSearch = false,
            CasePreservedNames = true,
            UnicodeOnDisk = true,
            PersistentAcls = false
        };

        var result = host.Mount(mountPoint);
        if (result < 0)
        {
            host.Dispose();
            throw new IOException($"WinFsp could not mount FediFile at '{mountPoint}'. Status: 0x{result:X8}.");
        }

        _host = host;
    }

    public void Unmount()
    {
        var host = _host;
        _host = null;
        host?.Dispose();
    }

    public void Dispose() => Unmount();
}
