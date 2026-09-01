using FediFile.Store;
using Fsp;

namespace FediFile.WinFsp;

public interface IWinFspFileSystemFactory
{
    FileSystemBase Create(IFediStore store);
}

public sealed class WinFspFileSystemFactory : IWinFspFileSystemFactory
{
    public FileSystemBase Create(IFediStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new WinFspReadOnlyFileSystem(store);
    }
}
