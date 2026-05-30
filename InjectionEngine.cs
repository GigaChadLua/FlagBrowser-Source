using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace FlagInjector;
public sealed class InjectionEngine : IDisposable
{
    const uint INJECT_ACCESS = 0x0020 | 0x0010 | 0x0008 | 0x1000;
    const uint OPEN_QUERY    = 0x0400;
    const uint TOKEN_QUERY   = 0x0008;
    const uint PAGE_READWRITE         = 0x04;
    const uint PAGE_EXECUTE_READWRITE = 0x40;
    const uint MEM_COMMIT             = 0x1000;
    const uint WRITABLE_MASK          = PAGE_READWRITE | PAGE_EXECUTE_READWRITE | 0x08 | 0x80;
    const int  TOKEN_ELEVATION        = 20;
    const nint MIN_VALID_PTR          = 0x10000;
    static readonly string[] Prefixes =
    {
        "DFFlag","SFFlag","FFlag",
        "DFInt","SFInt","FInt",
        "DFLog","SFLog","FLog",
        "DFString","SFString","FString",
        "DFDouble","FDouble"
    };
    static readonly string _localAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string? _cachedSettingsPath;
    IntPtr _hProc   = IntPtr.Zero;
    bool   _disposed;
    readonly Dictionary<string, (nint addr, byte[] original)> _originalBytes =
        new(StringComparer.OrdinalIgnoreCase);
    byte[] _readBuf = new byte[8];
    static readonly Random _rng = new();
    public nint BaseAddress { get; private set; }
    public int  Pid         { get; private set; }
    public bool IsAttached  => _hProc != IntPtr.Zero && BaseAddress != 0;
    public bool HasOriginals => _originalBytes.Count > 0;
    public event Action<int, nint>? OnAttached;
    public event Action?            OnDetached;
    public event Action?            OnDetachCleanup;  
    public bool TryAttach(int pid, nint baseAddr)
    {
        if (_hProc != IntPtr.Zero) Obf.CloseHandle(_hProc);
        _hProc      = Obf.NtOpenProcess(pid, INJECT_ACCESS);
        Pid         = pid;
        BaseAddress = baseAddr;
        if (_hProc == IntPtr.Zero || baseAddr == 0) { Detach(); return false; }
        OnAttached?.Invoke(pid, baseAddr);
        return true;
    }
    public void Detach()
    {
        OnDetachCleanup?.Invoke();
        if (_hProc != IntPtr.Zero) { Obf.CloseHandle(_hProc); _hProc = IntPtr.Zero; }
        BaseAddress = 0;
        Pid         = 0;
        _originalBytes.Clear();
        OnDetached?.Invoke();
    }
    public bool IsGameReady()
    {
        if (!IsAttached) return false;
        try
        {
            if (!Obf.GetProcessTimes(_hProc, out long created, out _, out _, out _))
                return true;
            double uptimeSec = (DateTime.UtcNow.ToFileTimeUtc() - created) / 1e7;
            return uptimeSec >= 5.0;
        }
        catch { return true; }
    }
    public bool IsTargetElevated()
    {
        if (Pid <= 0) return false;
        IntPtr hProc = IntPtr.Zero, hToken = IntPtr.Zero;
        try
        {
            hProc = Obf.NtOpenProcess(Pid, OPEN_QUERY);
            if (hProc == IntPtr.Zero) return false;
            if (!Obf.OpenProcessToken(hProc, TOKEN_QUERY, out hToken)) return false;
            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                return Obf.GetTokenInformation(hToken, TOKEN_ELEVATION, buf, 4, out _)
                    && Marshal.ReadInt32(buf) != 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return false; }
        finally
        {
            if (hToken != IntPtr.Zero) Obf.CloseHandle(hToken);
            if (hProc  != IntPtr.Zero) Obf.CloseHandle(hProc);
        }
    }
    static readonly Random _jitter = new();
    List<IntPtr> PrepareCtx()
    {
        var handles = new List<IntPtr>();
        foreach (int tid in Obf.GetThreadIds(Pid))
        {
            IntPtr ht = Obf.NtOpenThread(tid);
            if (ht == IntPtr.Zero) continue;
            Obf.NtSuspendThread(ht, out _);
            handles.Add(ht);
        }
        return handles;
    }
    static void RestoreCtx(List<IntPtr> handles)
    {
        foreach (var ht in handles) { Obf.NtResumeThread(ht, out _); Obf.CloseHandle(ht); }
    }
    public (int applied, int noOffset, int writeFail, int diskFallback) WriteAll(
        IReadOnlyList<FlagEntry>  flags,
        Dictionary<string, nint>  offsets,
        OffsetlessScanner?        scanner,
        FeatureSettings           feat)
    {
        if (!IsAttached) return (0, 0, 0, 0);
        int applied = 0, noOffset = 0, writeFail = 0, diskFallback = 0;
        var snapshot     = new List<FlagEntry>(flags);
        if (feat.ShuffleEnabled) Shuffle(snapshot);
        int enabledCount = snapshot.Count(f => f.Enabled);
        bool useSuspend  = enabledCount > feat.BatchSize;
        Dictionary<string, string>? diskFlags    = null;
        List<IntPtr>?               threadHndles = null;
        try
        {
            if (useSuspend) threadHndles = PrepareCtx();
            int batch = 0;
            foreach (var f in snapshot)
            {
                if (!f.Enabled) continue;
                bool ok = false;
                if (LookupRva(f.Name, offsets, out nint off))
                {
                    nint getSet   = BaseAddress + off;
                    nint valuePtr = ResolveValuePtr(_hProc, getSet, f.Type);
                    if (valuePtr != 0)
                    {
                        CacheRegion(f.Name, valuePtr, f.Type);
                        ok = PatchMem(_hProc, valuePtr, f.Value, f.Type);
                    }

                    if (!ok && feat.OffsetlessEnabled && scanner is not null)
                        ok = scanner.WriteFlag(_hProc, BaseAddress, f.Name, f.Value, f.Type, PatchMem);

                    if (!ok && !LooksLikeValueGetSet(_hProc, getSet, f.Type))
                    {
                        CacheRegion(f.Name, getSet, f.Type);
                        ok = PatchMem(_hProc, getSet, f.Value, f.Type);
                    }

                    if (ok) applied++; else writeFail++;
                }
                else if (feat.OffsetlessEnabled && scanner is not null
                      && scanner.WriteFlag(_hProc, BaseAddress, f.Name, f.Value, f.Type, PatchMem))
                {
                    applied++; ok = true;
                }
                else noOffset++;
                if (!ok && feat.DiskFallbackEnabled)
                    (diskFlags ??= new(StringComparer.OrdinalIgnoreCase))[f.Name] = f.Value;
                if (!useSuspend && feat.BatchDelayMs > 0 && ++batch >= feat.BatchSize)
                {
                    int d = feat.BatchDelayMs;
                    Thread.Sleep(d + _jitter.Next(-(d / 4), d / 4 + 1));
                    batch = 0;
                }
            }
            if (diskFlags?.Count > 0) diskFallback = FlushDisk(diskFlags);
        }
        finally
        {
            if (threadHndles is not null) RestoreCtx(threadHndles);
        }
        return (applied, noOffset, writeFail, diskFallback);
    }
    public bool WriteOne(
        FlagEntry                f,
        Dictionary<string, nint> offsets,
        OffsetlessScanner?       scanner,
        bool                     offsetlessEnabled,
        bool                     diskFallback = false)
    {
        if (!IsAttached || !f.Enabled) return false;
        if (LookupRva(f.Name, offsets, out nint off))
        {
            nint getSet   = BaseAddress + off;
            nint valuePtr = ResolveValuePtr(_hProc, getSet, f.Type);
            bool ok = false;
            if (valuePtr != 0)
            {
                CacheRegion(f.Name, valuePtr, f.Type);
                ok = PatchMem(_hProc, valuePtr, f.Value, f.Type);
            }
            if (!ok && offsetlessEnabled && scanner is not null)
                ok = scanner.WriteFlag(_hProc, BaseAddress, f.Name, f.Value, f.Type, PatchMem);
            if (!ok && !LooksLikeValueGetSet(_hProc, getSet, f.Type))
            {
                CacheRegion(f.Name, getSet, f.Type);
                ok = PatchMem(_hProc, getSet, f.Value, f.Type);
            }
            if (!ok && diskFallback) FlushDisk(new Dictionary<string,string>{{f.Name, f.Value}});
            return ok;
        }
        if (offsetlessEnabled && scanner is not null)
        {
            bool ok = scanner.WriteFlag(_hProc, BaseAddress, f.Name, f.Value, f.Type, PatchMem);
            if (!ok && diskFallback) FlushDisk(new Dictionary<string,string>{{f.Name, f.Value}});
            return ok;
        }
        if (diskFallback) FlushDisk(new Dictionary<string,string>{{f.Name, f.Value}});
        return false;
    }
    public int Restore()
    {
        if (!IsAttached || _originalBytes.Count == 0) return 0;
        int restored = 0;
        var threads = PrepareCtx();
        try
        {
            foreach (var (_, (addr, original)) in _originalBytes)
            {
                try
                {
                    if (CheckAccess(_hProc, (IntPtr)addr, original.Length, out bool restore, out uint oldProt))
                    {
                        Obf.NtWrite(_hProc, (IntPtr)addr, original, (uint)original.Length, out _);
                        if (restore) Obf.VirtualProtectEx(_hProc, (IntPtr)addr, original.Length, oldProt, out _);
                        restored++;
                    }
                }
                catch { }
            }
        }
        finally
        {
            RestoreCtx(threads);
            _originalBytes.Clear();
        }
        return restored;
    }
    public bool RestoreOne(string flagName)
    {
        if (!IsAttached) return false;
        if (!_originalBytes.TryGetValue(flagName, out var saved)) return false;
        var (addr, original) = saved;
        try
        {
            if (!CheckAccess(_hProc, (IntPtr)addr, original.Length, out bool restore, out uint oldProt))
                return false;
            Obf.NtWrite(_hProc, (IntPtr)addr, original, (uint)original.Length, out _);
            if (restore) Obf.VirtualProtectEx(_hProc, (IntPtr)addr, original.Length, oldProt, out _);
            _originalBytes.Remove(flagName);
            return true;
        }
        catch { return false; }
    }
    void CacheRegion(string name, nint addr, string type)
    {
        if (_originalBytes.ContainsKey(name)) return;
        int size = type switch { "bool" => 1, "int" => 4, "float" => 8, _ => 0 };
        if (size == 0) return;
        var buf = new byte[size];
        if (Obf.NtRead(_hProc, (IntPtr)addr, buf, (uint)size, out uint r) == 0 && r == (uint)size)
            _originalBytes[name] = (addr, buf);
    }
    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
    public bool PatchMem(IntPtr h, nint addr, string val, string type)
    {
        try
        {
            if (type == "string") return PatchStr(h, addr, val);
            byte[] buf = MkBuf(val, type);
            if (buf.Length == 0) return false;
            if (!CheckAccess(h, (IntPtr)addr, buf.Length, out bool restore, out uint oldProt)) return false;
            if (_readBuf.Length < buf.Length) _readBuf = new byte[buf.Length];
            bool success = false;
            for (int attempt = 1; attempt <= 3 && !success; attempt++)
            {
                if (Obf.NtWrite(h, (IntPtr)addr, buf, (uint)buf.Length, out _) != 0)
                { if (attempt < 3) Thread.Sleep(attempt * 50); continue; }
                if (Obf.NtRead(h, (IntPtr)addr, _readBuf, (uint)buf.Length, out uint nr) == 0
                    && nr == (uint)buf.Length
                    && _readBuf.AsSpan(0, buf.Length).SequenceEqual(buf))
                    success = true;
                else if (attempt < 3)
                    Thread.Sleep(attempt * 50);
            }
            if (restore) Obf.VirtualProtectEx(h, (IntPtr)addr, buf.Length, oldProt, out _);
            return success;
        }
        catch { return false; }
    }
    bool PatchStr(IntPtr h, nint valueInst, string val)
    {
        try
        {
            var header = new byte[24];
            if (Obf.NtRead(h, (IntPtr)valueInst, header, 24, out uint r) != 0 || r < 24) return false;
            nint  bufPtr   = (nint)BitConverter.ToInt64(header, 0);
            ulong capacity = BitConverter.ToUInt64(header, 16);
            if (bufPtr < MIN_VALID_PTR) return false;
            byte[] encoded = Encoding.UTF8.GetBytes(val);
            if ((ulong)encoded.Length > capacity) return false;
            byte[] toWrite = new byte[encoded.Length + 1];
            encoded.CopyTo(toWrite, 0);
            if (!CheckAccess(h, (IntPtr)bufPtr, toWrite.Length, out bool restore, out uint oldProt)) return false;
            int st = Obf.NtWrite(h, (IntPtr)bufPtr, toWrite, (uint)toWrite.Length, out _);
            if (restore) Obf.VirtualProtectEx(h, (IntPtr)bufPtr, toWrite.Length, oldProt, out _);
            if (st != 0) return false;
            byte[] newLen = BitConverter.GetBytes((ulong)encoded.Length);
            if (CheckAccess(h, (IntPtr)(valueInst + 8), 8, out restore, out oldProt))
            {
                Obf.NtWrite(h, (IntPtr)(valueInst + 8), newLen, 8, out _);
                if (restore) Obf.VirtualProtectEx(h, (IntPtr)(valueInst + 8), 8, oldProt, out _);
            }
            return true;
        }
        catch { return false; }
    }
    public bool WriteValue(IntPtr h, nint addr, string val, string type)
        => PatchMem(h, addr, val, type);
    static readonly byte[] _bTrue  = { 1 };
    static readonly byte[] _bFalse = { 0 };
    static byte[] MkBuf(string val, string type)
    {
        switch (type)
        {
            case "bool":
                return val.ToLowerInvariant() is "true" or "1" ? _bTrue : _bFalse;
            case "int":
                if (int.TryParse(val, out int n)) return BitConverter.GetBytes(n);
                if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(val[2..], System.Globalization.NumberStyles.HexNumber, null, out n))
                    return BitConverter.GetBytes(n);
                return Array.Empty<byte>();
            case "float":
                if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return BitConverter.GetBytes(d);
                return Array.Empty<byte>();
            default:
                return Array.Empty<byte>();
        }
    }
    static bool CheckAccess(IntPtr h, IntPtr addr, int size, out bool restore, out uint oldProt)
    {
        restore = false; oldProt = 0;
        if (!Obf.VirtualQueryEx(h, addr, out var mbi, (uint)Marshal.SizeOf<MBI>())) return false;
        if ((mbi.State & MEM_COMMIT) == 0) return false;
        if ((mbi.Protect & WRITABLE_MASK) != 0) return true;  
        if (!Obf.VirtualProtectEx(h, addr, size, PAGE_READWRITE, out oldProt)) return false;
        restore = true;
        return true;
    }
    public static int FlushDisk(Dictionary<string, string> flags)
    {
        if (flags.Count == 0) return 0;
        try
        {
            string? path = FindClientAppSettings();
            if (path is null) return 0;
            var existing = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                try
                {
                    var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
                    if (raw is not null)
                        foreach (var kv in raw) existing[kv.Key] = kv.Value.ToString();
                }
                catch { }
            }
            foreach (var kv in flags) existing[kv.Key] = kv.Value;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));
            return flags.Count;
        }
        catch { return 0; }
    }
    static string? FindClientAppSettings()
    {
        if (_cachedSettingsPath is not null) return _cachedSettingsPath;
        string versionsDir = Path.Combine(_localAppData, "Roblox", "Versions");
        if (Directory.Exists(versionsDir))
        {
            var ver = Directory.GetDirectories(versionsDir, "version-*")
                               .OrderByDescending(Directory.GetLastWriteTime)
                               .FirstOrDefault();
            if (ver is not null)
            {
                string candidate = Path.Combine(ver, "ClientSettings", "ClientAppSettings.json");
                if (Directory.Exists(Path.GetDirectoryName(candidate)!))
                    return _cachedSettingsPath = candidate;
            }
        }
        string pkgs = Path.Combine(_localAppData, "Packages");
        if (Directory.Exists(pkgs))
        {
            var uwp = Directory.GetDirectories(pkgs, "ROBLOXCORPORATION*").FirstOrDefault();
            if (uwp is not null)
            {
                string candidate = Path.Combine(uwp, "LocalCache", "Local", "ClientSettings", "ClientAppSettings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
                return _cachedSettingsPath = candidate;
            }
        }
        return null;
    }
    static nint ResolveValuePtr(IntPtr h, nint getSet, string type)
    {
        if (getSet < MIN_VALID_PTR) return 0;
        var buf = new byte[8];
        if (Obf.NtRead(h, (IntPtr)(getSet + 0x08), buf, 8, out uint r) != 0 || r != 8)
            return 0;
        nint valuePtr = (nint)BitConverter.ToInt64(buf, 0);
        return valuePtr >= MIN_VALID_PTR ? valuePtr : 0;
    }
    static bool LooksLikeValueGetSet(IntPtr h, nint addr, string expectedType)
    {
        if (addr < MIN_VALID_PTR) return false;
        var buf = new byte[4];
        if (Obf.NtRead(h, (IntPtr)(addr + 0x50), buf, 4, out uint r) != 0 || r != 4)
            return false;
        int typeId = BitConverter.ToInt32(buf, 0);
        string? actual = typeId switch
        {
            0 => "bool",
            1 => "int",
            2 => "float",
            3 => "string",
            _ => null
        };
        return actual is not null && actual.Equals(expectedType, StringComparison.OrdinalIgnoreCase);
    }

    public static bool LookupRva(string name, Dictionary<string, nint> offsets, out nint offset)
    {
        if (offsets.TryGetValue(name, out offset)) return true;
        foreach (var p in Prefixes)
        {
            if (!name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) continue;
            string bare = name[p.Length..];
            if (offsets.TryGetValue(bare, out offset)) return true;
            foreach (var p2 in Prefixes)
                if (offsets.TryGetValue(p2 + bare, out offset)) return true;
            break; 
        }
        foreach (var p in Prefixes)
            if (offsets.TryGetValue(p + name, out offset)) return true;
        offset = 0;
        return false;
    }
    public static string StripPrefix(string name)
    {
        foreach (var p in Prefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return name[p.Length..];
        return name;
    }
    public static string InferTypeFromName(string name)
    {
        foreach (var p in Prefixes)
        {
            if (!name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) continue;
            return p switch
            {
                var s when s.Contains("Flag")   => "bool",
                var s when s.Contains("Int")
                        || s.Contains("Log")    => "int",
                var s when s.Contains("Double") => "float",
                var s when s.Contains("String") => "string",
                _                               => ""
            };
        }
        return "";
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }
}
