using System.Text;
namespace FlagInjector;
public sealed class OffsetlessScanner
{
    const nint OFF_MAP_END  = 0x00;
    const nint OFF_MAP_LIST = 0x10;
    const nint OFF_MAP_MASK = 0x28;

    const nint OFF_NODE_FORWARD = 0x00;
    const nint OFF_NODE_STRING  = 0x10;
    const nint OFF_NODE_GETSET  = 0x40;

    const nint OFF_VGS_VALUEPTR = 0x08;
    const nint OFF_VGS_TYPE     = 0x50;

    const nint OFF_STR_PTR   = 0x00;
    const nint OFF_STR_SIZE  = 0x10;
    const nint OFF_STR_ALLOC = 0x18;

    static readonly HashSet<nint> UNREGISTERED_SENTINELS = new()
    {
        (nint)0x65757254L,
        (nint)0x31303031L,
    };

    const nint  MIN_VALID_PTR = 0x10000;
    const ulong FNV_BASIS     = 0xcbf29ce484222325UL;
    const ulong FNV_PRIME     = 0x100000001b3UL;

    nint _baseRva;
    public nint BaseRva
    {
        get => _baseRva;
        set { if (_baseRva != value) { _baseRva = value; ClearCache(); } }
    }
    public nint ListPointer { get => BaseRva; set => BaseRva = value; }
    public nint ToFlag  { get; set; } = 0x40;
    public nint ToValue { get; set; } = 0x08;

    readonly Dictionary<string, nint> _valueCache    = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, nint> _getSetCache   = new(StringComparer.OrdinalIgnoreCase);
    nint _cachedHashMap;
    (nint list, nint end, ulong mask) _mapId;

    public void ClearCache()
    {
        _valueCache.Clear();
        _getSetCache.Clear();
        _cachedHashMap = 0;
        _mapId = (0, 0, 0);
    }

    public bool SyncRva(Dictionary<string, nint> offsets)
    {
        foreach (var key in new[] { "FFlagList", "FlagList", "BaseRva", "flogDataBank", "fflaglist", "Pointer" })
            if (offsets.TryGetValue(key, out nint v) && v > MIN_VALID_PTR)
            { BaseRva = v; return true; }
        return false;
    }

    public nint Resolve(IntPtr hProc, nint moduleBase, string flagName)
    {
        if (BaseRva == 0 || hProc == IntPtr.Zero || moduleBase == 0) return 0;

        if (_valueCache.TryGetValue(flagName, out nint cached) && Probe(hProc, cached))
            return cached;
        _valueCache.Remove(flagName);

        nint hashMap = GetMap(hProc, moduleBase);
        if (hashMap == 0) return 0;

        var mapBytes = new byte[56];
        if (Obf.NtRead(hProc, (IntPtr)hashMap, mapBytes, 56, out uint ur) != 0 || ur < 48)
            return 0;

        nint  end  = (nint)LE64(mapBytes, OFF_MAP_END);
        nint  lst  = (nint)LE64(mapBytes, OFF_MAP_LIST);
        ulong mask = LE64(mapBytes, OFF_MAP_MASK);

        if (mask == 0 || lst < MIN_VALID_PTR || (mask & (mask + 1)) != 0) return 0;

        var newId = (lst, end, mask);
        if (newId != _mapId) { _valueCache.Clear(); _getSetCache.Clear(); _mapId = newId; }

        ulong bucketIdx = FNV1a(flagName) & mask;
        nint  bucket    = lst + (nint)(bucketIdx * 16);

        var bdata = new byte[16];
        if (Obf.NtRead(hProc, (IntPtr)bucket, bdata, 16, out ur) != 0 || ur < 16) return 0;

        nint current = (nint)LE64(bdata, 8);
        if (current == 0 || current == end || current < MIN_VALID_PTR) return 0;

        var  visited = new HashSet<nint>();
        int  steps   = 0;

        while (current != 0 && current != end && current >= MIN_VALID_PTR
               && visited.Add(current) && steps++ < 128)
        {
            var node = new byte[0x58];
            if (Obf.NtRead(hProc, (IntPtr)current, node, 0x58, out ur) != 0 || ur < 0x48) break;

            nint fwd = (nint)LE64(node, OFF_NODE_FORWARD);

            string? name = ReadStr(hProc, node, OFF_NODE_STRING);
            if (name is not null && name.Equals(flagName, StringComparison.OrdinalIgnoreCase))
            {
                nint getSet = (nint)LE64(node, OFF_NODE_GETSET);
                if (getSet < MIN_VALID_PTR) break;

                nint valuePtr = ReadPtr(hProc, getSet + OFF_VGS_VALUEPTR);

                if (valuePtr < MIN_VALID_PTR || UNREGISTERED_SENTINELS.Contains(valuePtr)) break;

                _getSetCache[flagName] = getSet;
                _valueCache[flagName]  = valuePtr;
                return valuePtr;
            }

            if (fwd == 0 || fwd == current) break;
            current = fwd;
        }
        return 0;
    }

    public string? ReadFlagType(IntPtr hProc, nint moduleBase, string flagName)
    {
        if (!_getSetCache.TryGetValue(flagName, out nint getSet))
        {
            Resolve(hProc, moduleBase, flagName);
            if (!_getSetCache.TryGetValue(flagName, out getSet)) return null;
        }

        var buf = new byte[4];
        if (Obf.NtRead(hProc, (IntPtr)(getSet + OFF_VGS_TYPE), buf, 4, out uint r) != 0 || r < 4)
            return null;

        return BitConverter.ToInt32(buf) switch
        {
            0 => "bool",
            1 => "int",
            2 => "float",
            3 => "string",
            _ => null
        };
    }

    public bool WriteFlag(IntPtr hProc, nint moduleBase,
        string flagName, string value, string type, WriteValueDelegate writer)
    {
        nint addr = Resolve(hProc, moduleBase, flagName);
        if (addr == 0) return false;
        return writer(hProc, addr, value, type);
    }

    nint GetMap(IntPtr hProc, nint moduleBase)
    {
        if (_cachedHashMap != 0 && Probe(hProc, _cachedHashMap)) return _cachedHashMap;
        _cachedHashMap = 0;
        nint fflagListPtr = ReadPtr(hProc, moduleBase + BaseRva);
        if (fflagListPtr < MIN_VALID_PTR) return 0;
        nint hashMap = fflagListPtr + 0x08;
        if (!Probe(hProc, hashMap)) return 0;
        _cachedHashMap = hashMap;
        return hashMap;
    }

    static string? ReadStr(IntPtr hProc, byte[] node, nint off)
    {
        int o = (int)off;
        if (o + 0x20 > node.Length) return null;
        ulong size  = LE64(node, off + OFF_STR_SIZE);
        ulong alloc = LE64(node, off + OFF_STR_ALLOC);
        if (size == 0 || size > 512) return null;
        byte[] b;
        if (alloc > 0x0F)
        {
            nint ptr = (nint)LE64(node, off + OFF_STR_PTR);
            if (ptr < MIN_VALID_PTR) return null;
            b = new byte[(int)size];
            if (Obf.NtRead(hProc, (IntPtr)ptr, b, (uint)size, out uint rd) != 0 || rd < (uint)size)
                return null;
        }
        else
        {
            int start = o;
            if (start + (int)size > node.Length) return null;
            b = node[start..(start + (int)size)];
        }
        for (int i = 0; i < Math.Min((int)size, 8); i++)
            if (b[i] < 0x20 || b[i] > 0x7E) return null;
        return Encoding.ASCII.GetString(b).TrimEnd('\0');
    }

    static nint ReadPtr(IntPtr h, nint addr)
    {
        var buf = new byte[8];
        return Obf.NtRead(h, (IntPtr)addr, buf, 8, out uint r) == 0 && r == 8
            ? (nint)BitConverter.ToInt64(buf, 0) : 0;
    }

    static ulong LE64(byte[] b, nint off) => BitConverter.ToUInt64(b, (int)off);

    static bool Probe(IntPtr h, nint addr)
    {
        if (addr < MIN_VALID_PTR) return false;
        var p = new byte[1];
        return Obf.NtRead(h, (IntPtr)addr, p, 1, out uint r) == 0 && r == 1;
    }

    public static ulong FNV1a(string name)
    {
        ulong h = FNV_BASIS;
        foreach (byte b in Encoding.ASCII.GetBytes(name)) { h ^= b; h *= FNV_PRIME; }
        return h;
    }
}
public delegate bool WriteValueDelegate(IntPtr h, nint addr, string val, string type);
