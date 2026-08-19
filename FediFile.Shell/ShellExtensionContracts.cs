using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FediFile.Store;

namespace FediFile.Shell;

[ComVisible(true)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0A7C2B27-3E43-4A1A-8844-2E16A7D9D901")]
public interface IFediShellFolder
{
    int ParseDisplayName(IntPtr hwnd, IntPtr bindContext, string displayName, ref uint eaten, out IntPtr pidl, ref uint attributes);
    int EnumObjects(IntPtr hwnd, uint flags, out IEnumIDList enumIdList);
    int BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr ppv);
    int BindToStorage(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr ppv);
    int CompareIDs(nint lParam, IntPtr pidl1, IntPtr pidl2);
    int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
    int GetAttributesOf(uint count, IntPtr apidl, ref uint attributes);
    int GetUIObjectOf(IntPtr hwndOwner, uint count, IntPtr apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv);
    int GetDisplayNameOf(IntPtr pidl, uint flags, out STRRET name);
    int SetNameOf(IntPtr hwnd, IntPtr pidl, string name, uint flags, out IntPtr newPidl);
}

[ComVisible(true)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("4D57B0F1-A177-4B0B-BC52-8399D26C7C01")]
public interface IFediShellItem
{
    int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    int GetParent(out IFediShellItem parent);
    int GetDisplayName(uint sigdnName, out IntPtr name);
    int GetAttributes(uint sfgaoMask, out uint attributes);
    int Compare(IFediShellItem item, uint hint, out int order);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class FediShellFolder : IFediShellFolder
{
    private readonly IFediStore _store;
    private readonly FediPath _path;

    public FediShellFolder(IFediStore store, FediPath path)
    {
        _store = store;
        _path = path;
    }

    public int ParseDisplayName(IntPtr hwnd, IntPtr bindContext, string displayName, ref uint eaten, out IntPtr pidl, ref uint attributes)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        eaten = (uint)displayName.Length;
        pidl = IntPtr.Zero;
        return 0;
    }

    public int EnumObjects(IntPtr hwnd, uint flags, out IEnumIDList enumIdList)
    {
        enumIdList = new FediEnumIdList(_store, _path);
        return 0;
    }

    public int BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr ppv)
    {
        ppv = IntPtr.Zero;
        return 0;
    }

    public int BindToStorage(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr ppv)
    {
        ppv = IntPtr.Zero;
        return unchecked((int)0x80004001);
    }

    public int CompareIDs(nint lParam, IntPtr pidl1, IntPtr pidl2) => 0;

    public int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv)
    {
        ppv = IntPtr.Zero;
        return unchecked((int)0x80004001);
    }

    public int GetAttributesOf(uint count, IntPtr apidl, ref uint attributes) => 0;

    public int GetUIObjectOf(IntPtr hwndOwner, uint count, IntPtr apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv)
    {
        ppv = IntPtr.Zero;
        return unchecked((int)0x80004001);
    }

    public int GetDisplayNameOf(IntPtr pidl, uint flags, out STRRET name)
    {
        name = new STRRET();
        return 0;
    }

    public int SetNameOf(IntPtr hwnd, IntPtr pidl, string name, uint flags, out IntPtr newPidl)
    {
        newPidl = IntPtr.Zero;
        return unchecked((int)0x80004001);
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class FediShellItem : IFediShellItem
{
    private readonly FediNode _node;

    public FediShellItem(FediNode node)
    {
        _node = node;
    }

    public int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv)
    {
        ppv = IntPtr.Zero;
        return unchecked((int)0x80004001);
    }

    public int GetParent(out IFediShellItem parent)
    {
        parent = null!;
        return unchecked((int)0x80004001);
    }

    public int GetDisplayName(uint sigdnName, out IntPtr name)
    {
        name = Marshal.StringToCoTaskMemUni(_node.Name);
        return 0;
    }

    public int GetAttributes(uint sfgaoMask, out uint attributes)
    {
        attributes = _node.IsDirectory ? 0x20000000u : 0x00000040u;
        return 0;
    }

    public int Compare(IFediShellItem item, uint hint, out int order)
    {
        order = 0;
        return 0;
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class FediEnumIdList : IEnumIDList
{
    private readonly IEnumerator<FediNode> _enumerator;

    public FediEnumIdList(IFediStore store, FediPath path)
    {
        ArgumentNullException.ThrowIfNull(store);

        _enumerator = store.ListDirectoryAsync(path, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult()
            .GetEnumerator();
    }

    public int NextItem(uint celt, out IntPtr rgelt, out uint pceltFetched)
    {
        if (_enumerator.MoveNext())
        {
            rgelt = Marshal.StringToCoTaskMemUni(_enumerator.Current.Name);
            pceltFetched = 1;
            return 0;
        }

        rgelt = IntPtr.Zero;
        pceltFetched = 0;
        return 1;
    }

    public int Skip(uint celt) => 0;

    public void Reset() => _enumerator.Reset();

    public void Clone(out IEnumIDList ppenum)
    {
        ppenum = this;
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class FediComStreamAdapter : IStream
{
    private readonly Stream _stream;

    public FediComStreamAdapter(Stream stream)
    {
        _stream = stream;
    }

    public void Read(byte[] pv, int cb, nint pcbRead)
    {
        var read = _stream.Read(pv, 0, cb);
        if (pcbRead != nint.Zero)
        {
            Marshal.WriteInt32(pcbRead, read);
        }
    }

    public void Write(byte[] pv, int cb, nint pcbWritten)
    {
        _stream.Write(pv, 0, cb);
        if (pcbWritten != nint.Zero)
        {
            Marshal.WriteInt32(pcbWritten, cb);
        }
    }

    public void Seek(long dlibMove, int dwOrigin, nint plibNewPosition)
    {
        var position = _stream.Seek(dlibMove, (SeekOrigin)dwOrigin);
        if (plibNewPosition != nint.Zero)
        {
            Marshal.WriteInt64(plibNewPosition, position);
        }
    }

    public void SetSize(long libNewSize) => _stream.SetLength(libNewSize);

    public void CopyTo(IStream pstm, long cb, nint pcbRead, nint pcbWritten) => throw new NotImplementedException();

    public void Commit(int grfCommitFlags) => _stream.Flush();

    public void Revert() => throw new NotSupportedException();

    public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

    public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

    public void Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        pstatstg = new STATSTG
        {
            cbSize = _stream.CanSeek ? _stream.Length : 0,
            type = 2
        };
    }

    public void Clone(out IStream ppstm) => ppstm = new FediComStreamAdapter(_stream);
}

[StructLayout(LayoutKind.Sequential)]
public struct STRRET : IEquatable<STRRET>
{
    public uint uType;
    public IntPtr pOleStr;

    public readonly bool Equals(STRRET other) => uType == other.uType && pOleStr == other.pOleStr;

    public override readonly bool Equals(object? obj) => obj is STRRET other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(uType, pOleStr);

    public static bool operator ==(STRRET left, STRRET right) => left.Equals(right);

    public static bool operator !=(STRRET left, STRRET right) => !(left == right);
}

[ComVisible(true)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F2-0000-0000-C000-000000000046")]
public interface IEnumIDList
{
    [PreserveSig]
    int NextItem(uint celt, out IntPtr rgelt, out uint pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    [PreserveSig]
    void Reset();

    [PreserveSig]
    void Clone(out IEnumIDList ppenum);
}
