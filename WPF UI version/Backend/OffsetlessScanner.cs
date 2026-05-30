using System.Text;

namespace FlagInjector;


























public sealed class OffsetlessScanner
{
    
    

    
    const nint OFF_MAP_END  = 0x00;   
    const nint OFF_MAP_LIST = 0x10;   
    const nint OFF_MAP_MASK = 0x28;   

    
    const nint OFF_ENTRY_FORWARD = 0x08;  
    const nint OFF_ENTRY_STRING  = 0x10;  
    const nint OFF_ENTRY_GETSET  = 0x30;  

    
    const nint OFF_STR_PTR   = 0x00;  
    const nint OFF_STR_SIZE  = 0x10;  
    const nint OFF_STR_ALLOC = 0x18;  

    
    
    
    
    
    static readonly HashSet<nint> UNREGISTERED_SENTINELS = new()
    {
        (nint)0x65757254L,
        (nint)0x31303031L,
    };

    const nint MIN_VALID_PTR = 0x10000;

    
    const ulong FNV_BASIS = 0xcbf29ce484222325UL;
    const ulong FNV_PRIME = 0x100000001b3UL;
    const int VALUE_CACHE_CAPACITY = 4096;
    const int MAX_CHAIN_MATCH_STEPS = 128;
    const int MAX_CHAIN_SAFETY_STEPS = 1000;

    static readonly int[] ENTRY_STRIDES = [64, 72, 56, 80, 88, 96];

    
    nint _flogDataBank;

    
    
    
    
    
    public nint FlogDataBank
    {
        get => _flogDataBank;
        set { if (_flogDataBank != value) { _flogDataBank = value; ClearCache(); } }
    }

    
    public nint ListPointer { get => FlogDataBank; set => FlogDataBank = value; }
    public nint ToFlag      { get; set; } = 0x30;
    public nint ToValue     { get; set; } = 0xC0;

    
    readonly Dictionary<string, nint> _valueCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, LinkedListNode<string>> _valueCacheLruNodes = new(StringComparer.OrdinalIgnoreCase);
    readonly LinkedList<string> _valueCacheLru = new();
    readonly Dictionary<string, NodeCacheEntry> _nodePtrCache = new(StringComparer.OrdinalIgnoreCase);
    nint _cachedHashMap;
    HashMapIdentity _cachedIdentity;

    public void ClearCache()
    {
        ClearLookupCaches();
        _cachedHashMap = 0;
        _cachedIdentity = default;
    }

    
    
    
    
    
    public bool TrySetFromOffsets(Dictionary<string, nint> offsets)
    {
        
        foreach (var key in new[] { "FFlagList", "FlagList", "FlogDataBank",
                                     "flogDataBank", "fflaglist" })
        {
            if (offsets.TryGetValue(key, out nint v) && v > MIN_VALID_PTR)
            {
                FlogDataBank = v;
                return true;
            }
        }
        return false;
    }

    

    public nint FindValueAddress(IntPtr hProc, nint moduleBase, string flagName)
    {
        if (FlogDataBank == 0 || hProc == IntPtr.Zero || moduleBase == 0) return 0;

        
        
        nint hashMap = GetOrRefreshHashMap(hProc, moduleBase);
        if (hashMap == 0) return 0;

        
        
        
        var mapBytes = new byte[56];
        if (Obf.NtRead(hProc, (IntPtr)hashMap, mapBytes, 56, out uint ur) != 0 || ur < 48)
            return 0;

        nint  end  = (nint)LE64(mapBytes, OFF_MAP_END);
        nint  lst  = (nint)LE64(mapBytes, OFF_MAP_LIST);
        ulong mask = LE64(mapBytes, OFF_MAP_MASK);

        
        if (mask == 0 || lst < MIN_VALID_PTR || (mask & (mask + 1)) != 0) return 0;

        var identity = new HashMapIdentity(lst, end, mask);
        if (!_cachedIdentity.IsEmpty && !_cachedIdentity.Equals(identity))
            ClearLookupCaches();
        _cachedIdentity = identity;

        if (TryGetCachedValue(hProc, flagName, out nint cached))
            return cached;

        ulong bucketIdx = FNV1a(flagName) & mask;

        if (_nodePtrCache.TryGetValue(flagName, out var nodeEntry) &&
            nodeEntry.Identity.Equals(identity) &&
            nodeEntry.BucketIndex == bucketIdx &&
            TryResolveValueFromNode(hProc, nodeEntry.NodePtr, flagName, out nint nodeCached))
        {
            AddValueCache(flagName, nodeCached);
            return nodeCached;
        }

        nint  bucket    = lst + (nint)(bucketIdx * 16);

        var bdata = new byte[16];
        if (Obf.NtRead(hProc, (IntPtr)bucket, bdata, 16, out ur) != 0 || ur < 16) return 0;
        nint current = (nint)LE64(bdata, 8);

        if (current == 0 || current == end) return 0;

        
        var  visited = new HashSet<nint>();
        int  matchSteps = 0;
        int  safetySteps = 0;

        while (current != 0 &&
               current != end &&
               visited.Add(current) &&
               matchSteps < MAX_CHAIN_MATCH_STEPS &&
               safetySteps++ < MAX_CHAIN_SAFETY_STEPS)
        {
            matchSteps++;
            
            
            if (!TryReadEntry(hProc, current, out var entry, out var fwd))
            {
                current = 0;
                continue;
            }

            
            string? ename = ReadStdString(hProc, entry, OFF_ENTRY_STRING);

            if (ename is not null &&
                ename.Equals(flagName, StringComparison.OrdinalIgnoreCase))
            {
                nint getSet = (nint)LE64(entry, OFF_ENTRY_GETSET);
                if (getSet < MIN_VALID_PTR) break;

                
                
                
                var getSetBuf = new byte[0xD8];
                if (Obf.NtRead(hProc, (IntPtr)getSet, getSetBuf, 0xD8, out ur) != 0 || ur < 0xC8)
                    break;

                foreach (nint off in GetValueOffsetCandidates())
                {
                    if ((int)off + 8 > (int)ur) continue;  
                    nint vptr = (nint)LE64(getSetBuf, off);

                    if (UNREGISTERED_SENTINELS.Contains(vptr)) continue;
                    if (vptr < MIN_VALID_PTR) continue;

                    _nodePtrCache[flagName] = new NodeCacheEntry(bucketIdx, current, identity);
                    AddValueCache(flagName, vptr);
                    return vptr;
                }
                break;  
            }

            current = fwd;
        }

        return 0;
    }

    IEnumerable<nint> GetValueOffsetCandidates()
    {
        yield return ToValue;

        if (ToValue != 0xC0)
            yield return 0xC0;

        if (ToValue != 0xD0)
            yield return 0xD0;
    }

    bool TryGetCachedValue(IntPtr hProc, string flagName, out nint value)
    {
        if (_valueCache.TryGetValue(flagName, out value))
        {
            if (IsPointerAlive(hProc, value))
            {
                TouchValueCache(flagName);
                return true;
            }
            RemoveValueCache(flagName);
        }

        value = 0;
        return false;
    }

    void AddValueCache(string flagName, nint value)
    {
        if (_valueCache.ContainsKey(flagName))
        {
            _valueCache[flagName] = value;
            TouchValueCache(flagName);
            return;
        }

        var node = _valueCacheLru.AddLast(flagName);
        _valueCacheLruNodes[flagName] = node;
        _valueCache[flagName] = value;

        while (_valueCache.Count > VALUE_CACHE_CAPACITY && _valueCacheLru.First is not null)
            RemoveValueCache(_valueCacheLru.First.Value);
    }

    void TouchValueCache(string flagName)
    {
        if (!_valueCacheLruNodes.TryGetValue(flagName, out var node)) return;
        _valueCacheLru.Remove(node);
        _valueCacheLru.AddLast(node);
    }

    void RemoveValueCache(string flagName)
    {
        _valueCache.Remove(flagName);
        if (_valueCacheLruNodes.Remove(flagName, out var node))
            _valueCacheLru.Remove(node);
    }

    void ClearLookupCaches()
    {
        _valueCache.Clear();
        _valueCacheLruNodes.Clear();
        _valueCacheLru.Clear();
        _nodePtrCache.Clear();
    }

    public bool WriteFlag(
        IntPtr hProc, nint moduleBase,
        string flagName, string value, string type,
        WriteValueDelegate writer)
    {
        nint addr = FindValueAddress(hProc, moduleBase, flagName);
        if (addr == 0) return false;
        return writer(hProc, addr, value, type);
    }

    bool TryResolveValueFromNode(IntPtr hProc, nint nodePtr, string flagName, out nint value)
    {
        value = 0;
        if (nodePtr < MIN_VALID_PTR) return false;
        if (!TryReadEntry(hProc, nodePtr, out var entry, out _)) return false;

        string? ename = ReadStdString(hProc, entry, OFF_ENTRY_STRING);
        return ename is not null &&
               ename.Equals(flagName, StringComparison.OrdinalIgnoreCase) &&
               TryResolveValueFromEntry(hProc, entry, out value);
    }

    static bool TryReadEntry(IntPtr hProc, nint entryPtr, out byte[] entry, out nint forward)
    {
        foreach (int stride in ENTRY_STRIDES)
        {
            entry = new byte[stride];
            if (Obf.NtRead(hProc, (IntPtr)entryPtr, entry, (uint)stride, out uint ur) == 0 && ur >= 56)
            {
                forward = (nint)LE64(entry, OFF_ENTRY_FORWARD);
                return true;
            }
        }

        entry = [];
        forward = 0;
        return false;
    }

    bool TryResolveValueFromEntry(IntPtr hProc, byte[] entry, out nint value)
    {
        value = 0;
        nint getSet = (nint)LE64(entry, OFF_ENTRY_GETSET);
        if (getSet < MIN_VALID_PTR) return false;

        var getSetBuf = new byte[0xD8];
        if (Obf.NtRead(hProc, (IntPtr)getSet, getSetBuf, 0xD8, out uint ur) != 0 || ur < 0xC8)
            return false;

        foreach (nint off in GetValueOffsetCandidates())
        {
            if ((int)off + 8 > (int)ur) continue;
            nint vptr = (nint)LE64(getSetBuf, off);

            if (UNREGISTERED_SENTINELS.Contains(vptr)) continue;
            if (vptr < MIN_VALID_PTR) continue;

            value = vptr;
            return true;
        }

        return false;
    }

    

    
    
    
    
    
    
    nint GetOrRefreshHashMap(IntPtr hProc, nint moduleBase)
    {
        if (_cachedHashMap != 0 && IsPointerAlive(hProc, _cachedHashMap))
            return _cachedHashMap;

        _cachedHashMap = 0;

        
        nint fflagListAddr = ReadPtr(hProc, moduleBase + FlogDataBank);
        if (fflagListAddr < MIN_VALID_PTR) return 0;

        
        nint hashMap = fflagListAddr + 0x08;
        if (!IsPointerAlive(hProc, hashMap)) return 0;

        _cachedHashMap = hashMap;
        return hashMap;
    }

    
    static string? ReadStdString(IntPtr hProc, byte[] entry, nint off)
    {
        int o = (int)off;
        if (o + 0x20 > entry.Length) return null;

        ulong size  = LE64(entry, off + OFF_STR_SIZE);
        ulong alloc = LE64(entry, off + OFF_STR_ALLOC);

        if (size == 0 || size > 512) return null;

        byte[] strBytes;

        if (alloc > 0x0F)
        {
            
            nint ptr = (nint)LE64(entry, off + OFF_STR_PTR);
            if (ptr < MIN_VALID_PTR) return null;
            strBytes = new byte[(int)size];
            if (Obf.NtRead(hProc, (IntPtr)ptr, strBytes, (uint)size, out uint rd) != 0 || rd < (uint)size)
                return null;
        }
        else
        {
            
            int start = o;
            if (start + (int)size > entry.Length) return null;
            strBytes = entry[start..(start + (int)size)];
        }

        
        int check = (int)Math.Min(size, 8);
        for (int i = 0; i < check; i++)
            if (strBytes[i] < 0x20 || strBytes[i] > 0x7E) return null;

        return Encoding.ASCII.GetString(strBytes).TrimEnd('\0');
    }

    

    static nint ReadPtr(IntPtr h, nint addr)
    {
        var buf = new byte[8];
        return Obf.NtRead(h, (IntPtr)addr, buf, 8, out uint r) == 0 && r == 8
            ? (nint)BitConverter.ToInt64(buf, 0) : 0;
    }

    static ulong LE64(byte[] b, nint off) =>
        BitConverter.ToUInt64(b, (int)off);

    static bool IsPointerAlive(IntPtr hProc, nint addr)
    {
        if (addr < MIN_VALID_PTR) return false;
        var p = new byte[1];
        return Obf.NtRead(hProc, (IntPtr)addr, p, 1, out uint r) == 0 && r == 1;
    }

    
    public static ulong FNV1a(string name)
    {
        ulong h = FNV_BASIS;
        foreach (byte b in Encoding.ASCII.GetBytes(name)) { h ^= b; h *= FNV_PRIME; }
        return h;
    }

    readonly record struct HashMapIdentity(nint MapList, nint MapEnd, ulong MapMask)
    {
        public bool IsEmpty => MapList == 0 && MapEnd == 0 && MapMask == 0;
    }

    readonly record struct NodeCacheEntry(ulong BucketIndex, nint NodePtr, HashMapIdentity Identity);
}

public delegate bool WriteValueDelegate(IntPtr h, nint addr, string val, string type);
