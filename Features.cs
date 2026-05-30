using System.Diagnostics;
using System.Runtime.InteropServices;
namespace FlagInjector;
public class FeatureSettings
{
    public bool SafeMode            { get; set; } = true;
    public bool ReApplyEnabled      { get; set; } = false;
    public int  ReApplyIntervalMs   { get; set; } = 5000;
    public bool RandomReApply       { get; set; } = false;
    public int  RandomMinMs         { get; set; } = 3000;
    public int  RandomMaxMs         { get; set; } = 10000;
    public bool TimingAttack        { get; set; } = false;
    public int  TimingDelayMs       { get; set; } = 300;
    public bool OffsetlessEnabled   { get; set; } = true;
    public bool StealthMode         { get; set; } = false;
    public bool DiskFallbackEnabled { get; set; } = false;
    public bool ShuffleEnabled      { get; set; } = false;
    public int  BatchSize           { get; set; } = 20;
    public int  BatchDelayMs        { get; set; } = 15;
    static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlagInjector", "features.json");
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
    public static FeatureSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return System.Text.Json.JsonSerializer.Deserialize<FeatureSettings>(
                    File.ReadAllText(_path)) ?? new();
        }
        catch { }
        return new();
    }
}
public class FeatureEngine : IDisposable
{
    static readonly HashSet<string> _recordingProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "obs64","obs32","obs","bandicam","fraps","dxtory",
        "action","plays","shadowplay","medal","overwolf",
        "xsplit","streamlabs","nvcaplayer","amdrsserv",
        "gamebar","gamecapture","xboxapp"
    };
    static HashSet<string>? _procSnapshot;
    static DateTime         _procSnapshotAt;
    readonly FeatureSettings _cfg;
    readonly Func<Task>      _applyAll;
    readonly Func<bool>      _isAttached;
    readonly Func<int>       _getRobloxPid;
    System.Threading.Timer? _reApplyTimer;
    System.Threading.Timer? _randomTimer;
    readonly Random _rng = new();
    bool _disposed;
    public FeatureEngine(
        FeatureSettings cfg,
        Func<Task>      applyAll,
        Func<bool>      isAttached,
        Func<int>?      getRobloxPid = null)
    {
        _cfg          = cfg;
        _applyAll     = applyAll;
        _isAttached   = isAttached;
        _getRobloxPid = getRobloxPid ?? (() => -1);
    }
    public void SetReApply(bool enabled)
    {
        _reApplyTimer?.Dispose();
        _reApplyTimer = null;
        if (!enabled) return;
        _reApplyTimer = new System.Threading.Timer(_ =>
        {
            if (_isAttached() && !CheckEnv()) _ = _applyAll();
        }, null, _cfg.ReApplyIntervalMs, _cfg.ReApplyIntervalMs);
    }
    public void SetRandomReApply(bool enabled)
    {
        _randomTimer?.Dispose();
        _randomTimer = null;
        if (!enabled) return;
        ScheduleNextRandom();
    }
    void ScheduleNextRandom()
    {
        int delay = _rng.Next(_cfg.RandomMinMs, _cfg.RandomMaxMs);
        _randomTimer = new System.Threading.Timer(_ =>
        {
            if (_isAttached() && !CheckEnv()) _ = _applyAll();
            _randomTimer?.Dispose();
            ScheduleNextRandom();
        }, null, delay, Timeout.Infinite);
    }
    public static async Task TimingAttackApply(
        Func<Task> applyAll, int delayMs, CancellationToken ct = default)
    {
        await Task.Delay(delayMs, ct);
        if (!ct.IsCancellationRequested) await applyAll();
    }
    public bool CheckPriv() =>
        _getRobloxPid() is int pid && pid > 0 && ElevationHelper.IsProcessElevated(pid);
    public static bool IsProcessElevated(int pid) =>
        ElevationHelper.IsProcessElevated(pid);
    public static bool IsSelfElevated() =>
        ElevationHelper.IsSelfElevated();
    public bool CheckEnv()
    {
        if (!_cfg.StealthMode) return false;
        var now = DateTime.UtcNow;
        if (_procSnapshot is null || (now - _procSnapshotAt).TotalSeconds > 2)
        {
            _procSnapshot = Process.GetProcesses()
                .Select(p => { try { return p.ProcessName; } catch { return ""; } })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _procSnapshotAt = now;
        }
        return _procSnapshot.Overlaps(_recordingProcesses);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reApplyTimer?.Dispose();
        _randomTimer?.Dispose();
    }
}
internal static class ElevationHelper
{
    const uint QUERY_INFO    = 0x0400;
    const uint TOKEN_QUERY   = 0x0008;
    const int  ELEVATION_CLS = 20;
    public static bool IsProcessElevated(int pid)
    {
        IntPtr hProc = IntPtr.Zero, hTok = IntPtr.Zero;
        try
        {
            hProc = Obf.NtOpenProcess(pid, QUERY_INFO);
            if (hProc == IntPtr.Zero) return false;
            if (!Obf.OpenProcessToken(hProc, TOKEN_QUERY, out hTok)) return false;
            IntPtr buf = Marshal.AllocHGlobal(4);
            try { return Obf.GetTokenInformation(hTok, ELEVATION_CLS, buf, 4, out _) && Marshal.ReadInt32(buf) != 0; }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return false; }
        finally
        {
            if (hTok  != IntPtr.Zero) Obf.CloseHandle(hTok);
            if (hProc != IntPtr.Zero) Obf.CloseHandle(hProc);
        }
    }
    public static bool IsSelfElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
