using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using FlagInjector;
using Microsoft.Win32;

namespace FlagInjectorWpf;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const int WmHotkey = 0x0312;
    const int HotkeyApply = 1;
    const int HotkeyFlagBase = 1000;
    const int ModAlt = 0x0001;
    const int ModControl = 0x0002;
    const int ModShift = 0x0004;
    const int ModNoRepeat = 0x4000;

    const string DefaultUrl1 = "https://imtheo.lol/Offsets/FFlags.cs";
    const string DefaultUrl2 = "https://npdrlaufeimrkvdnjijl.supabase.co/functions/v1/get-offsets";
    const string ExtraOffsetsUrl = "https://raw.githubusercontent.com/soulukr78/BestRobloxOffsets/refs/heads/main/BestRobloxOffsets";

    readonly InjectionEngine _engine = new();
    readonly OffsetLoader _loader = new();
    readonly OffsetlessScanner _scanner = new();
    readonly ProfileManager _profiles = new();
    readonly AppSettings _appCfg = AppSettings.Load();
    readonly FeatureSettings _features = FeatureSettings.Load();
    readonly DispatcherTimer _robloxTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    FeatureEngine? _featureEngine;
    HwndSource? _source;
    bool _applyHotkeyRegistered;
    bool _capturingHotkey;

    readonly List<string> _allAvailableNames = new();
    readonly List<FlagEntry> _flags = new();
    readonly Dictionary<int, int> _flagHotkeys = new();

    public ObservableCollection<AvailableFlagRow> AvailableFlags { get; } = new();
    public ObservableCollection<ModifiedFlagRow> ModifiedFlags { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _featureEngine = new FeatureEngine(_features, ApplyAllAsync, () => _engine.IsAttached, () => _engine.Pid);
        ApplyFeatureRuntimeSettings();

        _engine.OnAttached += (pid, _) => Dispatcher.Invoke(() =>
        {
            RobloxStateText.Text = "Attached";
            RobloxStateText.Foreground = (System.Windows.Media.Brush)FindResource("Green");
            RobloxPidText.Text = $"PID {pid}";
            SetStatus("Roblox attached.");
        });
        _engine.OnDetached += () => Dispatcher.Invoke(() =>
        {
            RobloxStateText.Text = "Not attached";
            RobloxStateText.Foreground = (System.Windows.Media.Brush)FindResource("Red");
            RobloxPidText.Text = "PID -";
        });
        _engine.OnDetachCleanup += () => _scanner.ClearCache();

        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            RegisterApplyHotkey();
            RegisterFlagHotkeys();
        };
        Loaded += async (_, _) =>
        {
            ApplyOffsetSettings();
            LoadDefaults();
            await LoadOffsetsAsync();
            StartRobloxMonitor();
        };
        Closed += (_, _) =>
        {
            UnregisterFlagHotkeys();
            UnregisterApplyHotkey();
            _source?.RemoveHook(WndProc);
            _robloxTimer.Stop();
            _featureEngine?.Dispose();
            _engine.Dispose();
        };
    }

    void ApplyFeatureRuntimeSettings()
    {
        _featureEngine?.SetReApply(_features.ReApplyEnabled);
        _featureEngine?.SetRandomReApply(_features.RandomReApply);
    }

    void ApplyOffsetSettings()
    {
        Url1Box.Text = string.IsNullOrWhiteSpace(_appCfg.Url1) ? DefaultUrl1 : _appCfg.Url1;
        Url2Box.Text = string.IsNullOrWhiteSpace(_appCfg.Url2) ? DefaultUrl2 : _appCfg.Url2;
    }

    void SaveOffsetSettings()
    {
        _appCfg.Url1 = Url1Box.Text.Trim();
        _appCfg.Url2 = Url2Box.Text.Trim();
        _appCfg.Save();
    }

    void LoadDefaults()
    {
        var (count, error) = FlagDefaults.Instance.LoadEmbedded();
        DefaultsStatusText.Text = error is null
            ? $"defaults: {count:N0} flags (built-in)"
            : $"defaults: error - {error}";
    }

    async Task LoadOffsetsAsync()
    {
        SetStatus("Loading offsets...");
        string url1 = string.IsNullOrWhiteSpace(Url1Box.Text) ? DefaultUrl1 : Url1Box.Text.Trim();
        string url2 = string.IsNullOrWhiteSpace(Url2Box.Text) ? DefaultUrl2 : Url2Box.Text.Trim();
        var urls = new[] { url1, url2, ExtraOffsetsUrl }
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var (count, errors) = await _loader.LoadUrlsAsync(urls);
        SaveOffsetSettings();
        SyncScannerFromLoader();

        _allAvailableNames.Clear();
        _allAvailableNames.AddRange(_loader.SortedNames);
        RefreshAvailable();

        SetStatus(count > 0
            ? $"OK: {count:N0} offsets loaded"
            : $"Offset load failed: {string.Join(" | ", errors)}");
    }

    void SyncScannerFromLoader()
    {
        if (_loader.FlogDataBank == 0)
            _scanner.TrySetFromOffsets(_loader.Offsets);
        else
            _scanner.FlogDataBank = _loader.FlogDataBank;

        _scanner.ToFlag = _loader.StructToFlag;
        _scanner.ToValue = _loader.StructToValue;
    }

    void RefreshAvailable()
    {
        string filter = AvailableSearchBox.Text.Trim();
        var inUse = _flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        AvailableFlags.Clear();
        foreach (string name in _allAvailableNames)
        {
            if (inUse.Contains(name)) continue;
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            AvailableFlags.Add(new AvailableFlagRow(name, CategoryFromName(name), TypeFromName(name)));
            if (AvailableFlags.Count >= 800) break;
        }
    }

    void RefreshModified()
    {
        string filter = ModifiedSearchBox.Text.Trim();
        ModifiedFlags.Clear();
        foreach (var flag in _flags)
        {
            if (filter.Length > 0 && !flag.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            ModifiedFlags.Add(new ModifiedFlagRow(flag));
        }
    }

    void AddSelectedFlag()
    {
        if (AvailableList.SelectedItem is not AvailableFlagRow row) return;
        string value = string.IsNullOrWhiteSpace(AddValueBox.Text) ? "true" : AddValueBox.Text.Trim();
        if (_flags.Any(f => f.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase))) return;

        var entry = new FlagEntry(row.Name, value)
        {
            DefaultValue = FlagDefaults.Instance.Get(row.Name)
        };
        _flags.Add(entry);

        if (_engine.IsAttached)
            _engine.ApplyOne(entry, _loader.Offsets, _scanner, _features.OffsetlessEnabled, _features.DiskFallbackEnabled);

        RefreshAvailable();
        RefreshModified();
        SelectModified(entry.Name);
        SetStatus($"Added: {entry.Name} = {entry.Value}");
    }

    void SelectModified(string name)
    {
        var row = ModifiedFlags.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (row is not null) ModifiedList.SelectedItem = row;
    }

    FlagEntry? SelectedFlag()
    {
        if (ModifiedList.SelectedItem is not ModifiedFlagRow row) return null;
        return _flags.FirstOrDefault(f => f.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
    }

    void PopulateEditFields(FlagEntry? flag)
    {
        UpdateValueBox.Text = flag?.Value ?? "";
        DefaultValueBox.Text = flag?.DefaultValue ?? "";
        HotkeyBox.Text = flag?.Hotkey ?? "";
    }

    async Task ApplyAllAsync()
    {
        if (!_engine.IsAttached)
        {
            SetStatus("Roblox not attached.");
            return;
        }

        SetStatus("Applying flags...");
        var snapshot = _flags.ToList();
        int enabled = snapshot.Count(f => f.Enabled);
        var result = await Task.Run(() => _engine.ApplyAll(snapshot, _loader.Offsets, _scanner, _features));

        var parts = new List<string> { $"OK: {result.applied}/{enabled} applied" };
        if (result.noOffset > 0) parts.Add($"{result.noOffset} without offset");
        if (result.writeFail > 0) parts.Add($"{result.writeFail} write failed");
        if (result.diskFallback > 0) parts.Add($"{result.diskFallback} via disk");
        SetStatus(string.Join(" | ", parts));
    }

    void StartRobloxMonitor()
    {
        _robloxTimer.Tick += (_, _) =>
        {
            try
            {
                var proc = Process.GetProcessesByName(Obf.ProcName).FirstOrDefault();
                if (proc is null)
                {
                    if (_engine.IsAttached) _engine.Dispose();
                    return;
                }

                if (!_engine.IsAttached || _engine.Pid != proc.Id)
                {
                    nint baseAddr = (nint)proc.MainModule!.BaseAddress;
                    _engine.TryAttach(proc.Id, baseAddr);
                }
            }
            catch
            {
            }
        };
        _robloxTimer.Start();
    }

    static string CategoryFromName(string name)
    {
        if (name.Contains("Render", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Texture", StringComparison.OrdinalIgnoreCase))
            return "Rendering";
        if (name.Contains("Network", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Replicator", StringComparison.OrdinalIgnoreCase))
            return "Network";
        if (name.Contains("Telemetry", StringComparison.OrdinalIgnoreCase))
            return "Telemetry";
        if (name.Contains("Cache", StringComparison.OrdinalIgnoreCase))
            return "Cache";
        return "Flag";
    }

    static string TypeFromName(string name)
    {
        foreach (string prefix in new[] { "DFFlag", "DFInt", "DFString", "FFlag", "FInt", "FString", "SFFlag" })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return prefix;

        return "Flag";
    }

    void SetStatus(string message) => StatusText.Text = message;

    async void LoadOffsets_Click(object sender, RoutedEventArgs e) => await LoadOffsetsAsync();
    void AvailableSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshAvailable();
    void ModifiedSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshModified();
    void AvailableList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
    void AvailableList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddSelectedFlag();
    void AddSelected_Click(object sender, RoutedEventArgs e) => AddSelectedFlag();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_features, _ =>
        {
            ApplyFeatureRuntimeSettings();
            SetStatus("Settings saved.");
        })
        {
            Owner = this
        };
        window.ShowDialog();
    }

    void ModifiedList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        PopulateEditFields(SelectedFlag());

    void UpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        var flag = SelectedFlag();
        if (flag is null) return;
        string value = UpdateValueBox.Text.Trim();
        if (value.Length == 0) return;
        flag.Update(value);
        if (_engine.IsAttached && flag.Enabled)
            _engine.ApplyOne(flag, _loader.Offsets, _scanner, _features.OffsetlessEnabled, _features.DiskFallbackEnabled);
        RefreshModified();
        SelectModified(flag.Name);
        SetStatus($"Updated: {flag.Name} = {flag.Value}");
    }

    void ToggleSelected_Click(object sender, RoutedEventArgs e)
    {
        var flag = SelectedFlag();
        if (flag is null) return;
        ApplyToggle(flag);
    }

    bool ApplyRestoreValue(FlagEntry flag)
    {
        string? restoreValue = flag.DefaultValue ?? (flag.OriginalValue.Length > 0 ? flag.OriginalValue : null);
        if (restoreValue is null) return false;
        var temp = new FlagEntry(flag.Name, restoreValue) { Type = flag.Type };
        return _engine.ApplyOne(temp, _loader.Offsets, _scanner, _features.OffsetlessEnabled, _features.DiskFallbackEnabled);
    }

    void ApplyToggle(FlagEntry flag)
    {
        flag.Enabled = !flag.Enabled;

        if (!flag.Enabled && _engine.IsAttached)
        {
            bool restored = _engine.UninjectOne(flag.Name) || ApplyRestoreValue(flag);
            RefreshModified();
            SelectModified(flag.Name);
            SetStatus(restored
                ? $"{flag.Name}: Disabled"
                : $"{flag.Name}: Disabled, but no original/default/initial value was available to restore");
            return;
        }

        if (flag.Enabled && _engine.IsAttached)
            _engine.ApplyOne(flag, _loader.Offsets, _scanner, _features.OffsetlessEnabled, _features.DiskFallbackEnabled);

        RefreshModified();
        SelectModified(flag.Name);
        SetStatus($"{flag.Name}: {(flag.Enabled ? "Enabled" : "Disabled")}");
    }

    void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var flag = SelectedFlag();
        if (flag is null) return;
        _flags.Remove(flag);
        RegisterFlagHotkeys();
        RefreshAvailable();
        RefreshModified();
        PopulateEditFields(null);
        SetStatus($"Removed: {flag.Name}");
    }

    void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        var flag = SelectedFlag();
        if (flag is null) return;
        flag.DefaultValue = string.IsNullOrWhiteSpace(DefaultValueBox.Text) ? null : DefaultValueBox.Text.Trim();
        RefreshModified();
        SelectModified(flag.Name);
    }

    void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        var flag = SelectedFlag();
        if (flag is null) return;
        var value = flag.DefaultValue ?? flag.OriginalValue;
        if (string.IsNullOrWhiteSpace(value)) return;
        flag.Update(value);
        UpdateValueBox.Text = flag.Value;
        if (_engine.IsAttached)
            _engine.ApplyOne(flag, _loader.Offsets, _scanner, _features.OffsetlessEnabled, _features.DiskFallbackEnabled);
        RefreshModified();
        SelectModified(flag.Name);
    }

    void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        var flag = SelectedFlag();
        if (flag is null) return;
        flag.Hotkey = "";
        HotkeyBox.Text = "";
        RegisterFlagHotkeys();
        if (_capturingHotkey) UnregisterFlagHotkeys();
        RefreshModified();
        SelectModified(flag.Name);
        SetStatus($"Hotkey cleared: {flag.Name}");
    }

    async void ApplyAll_Click(object sender, RoutedEventArgs e) => await ApplyAllAsync();

    void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        _profiles.Save(new Profile { Name = "Default", Flags = _flags.ToList() });
        SetStatus($"Profile 'Default' saved ({_flags.Count} flags).");
    }

    void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = _profiles.Load("Default");
        if (profile is null)
        {
            SetStatus("Profile 'Default' not found.");
            return;
        }

        _flags.Clear();
        foreach (var flag in profile.Flags)
        {
            flag.DefaultValue ??= FlagDefaults.Instance.Get(flag.Name);
            _flags.Add(flag);
        }
        RegisterFlagHotkeys();
        RefreshAvailable();
        RefreshModified();
        SetStatus($"Profile 'Default' loaded ({_flags.Count} flags).");
    }

    void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON Files|*.json|All Files|*.*" };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = FlagParser.Parse(File.ReadAllText(dialog.FileName));
            if (imported.Count == 0)
            {
                SetStatus("Import failed: no flags found.");
                return;
            }

            var (added, skipped) = FlagParser.MergeInto(_flags, imported);
            RegisterFlagHotkeys();
            RefreshAvailable();
            RefreshModified();
            SetStatus($"Imported {added} flags. Skipped {skipped} duplicates.");
        }
        catch (Exception ex)
        {
            SetStatus($"Import failed: {ex.Message}");
        }
    }

    void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON Files|*.json", FileName = "flags_export.json" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, FlagParser.ToJson(_flags));
        SetStatus($"Exported {_flags.Count} flags.");
    }

    void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey) return;
        _capturingHotkey = true;
        UnregisterApplyHotkey();
        UnregisterFlagHotkeys();
        SetStatus("Press Ctrl/Alt/Shift plus a key, or F1-F12. F8 is reserved for Apply.");
    }

    void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_capturingHotkey) return;
        _capturingHotkey = false;
        RegisterApplyHotkey();
        RegisterFlagHotkeys();
    }

    void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var flag = SelectedFlag();
        if (flag is null) return;

        Key key = RealKey(e);
        if (IsModifierKey(key)) return;

        int modifiers = ModifiersFromKeyboard(Keyboard.Modifiers);
        bool bareFunctionKey = IsBareFunctionKey(key);
        if (modifiers == 0 && !bareFunctionKey)
        {
            SetStatus("Use Ctrl/Alt/Shift, or F1-F12. F8 is reserved for Apply.");
            return;
        }

        if (modifiers == 0 && key == Key.F8)
        {
            SetStatus("F8 is reserved for Apply.");
            return;
        }

        string hotkey = FormatHotkey(modifiers, key);
        int selectedIndex = _flags.IndexOf(flag);
        int existing = _flags.FindIndex(f => string.Equals(f.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0 && existing != selectedIndex)
        {
            SetStatus($"Hotkey already used by {_flags[existing].Name}");
            return;
        }

        string previousHotkey = flag.Hotkey;
        flag.Hotkey = hotkey;
        HotkeyBox.Text = hotkey;
        bool registered = RegisterFlagHotkeys();
        if (_capturingHotkey) UnregisterFlagHotkeys();
        if (!registered)
        {
            flag.Hotkey = previousHotkey;
            HotkeyBox.Text = previousHotkey;
            if (!_capturingHotkey) RegisterFlagHotkeys();
        }

        RefreshModified();
        SelectModified(flag.Name);
        SetStatus(registered ? $"Hotkey set: {flag.Name} -> {hotkey}" : $"Hotkey unavailable: {hotkey}");
    }

    void RegisterApplyHotkey()
    {
        if (_applyHotkeyRegistered || _source is null) return;
        _applyHotkeyRegistered = RegisterHotKey(_source.Handle, HotkeyApply, ModNoRepeat, KeyInterop.VirtualKeyFromKey(Key.F8));
    }

    void UnregisterApplyHotkey()
    {
        if (!_applyHotkeyRegistered || _source is null) return;
        UnregisterHotKey(_source.Handle, HotkeyApply);
        _applyHotkeyRegistered = false;
    }

    bool RegisterFlagHotkeys()
    {
        UnregisterFlagHotkeys();
        if (_source is null) return true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool allRegistered = true;
        for (int i = 0; i < _flags.Count; i++)
        {
            string hotkey = (_flags[i].Hotkey ?? "").Trim();
            if (hotkey.Length == 0) continue;
            if (!TryParseHotkey(hotkey, out int modifiers, out Key key))
            {
                allRegistered = false;
                continue;
            }

            string normalized = FormatHotkey(modifiers, key);
            if (!seen.Add(normalized))
            {
                allRegistered = false;
                SetStatus($"Hotkey duplicated: {normalized}");
                continue;
            }

            int id = HotkeyFlagBase + i;
            if (RegisterHotKey(_source.Handle, id, modifiers | ModNoRepeat, KeyInterop.VirtualKeyFromKey(key)))
                _flagHotkeys[id] = i;
            else
            {
                allRegistered = false;
                SetStatus($"Hotkey busy: {normalized}");
            }
        }

        return allRegistered;
    }

    void UnregisterFlagHotkeys()
    {
        if (_source is null)
        {
            _flagHotkeys.Clear();
            return;
        }

        foreach (int id in _flagHotkeys.Keys.ToList())
            UnregisterHotKey(_source.Handle, id);
        _flagHotkeys.Clear();
    }

    void ToggleFlagAt(int index)
    {
        if (index < 0 || index >= _flags.Count) return;
        ApplyToggle(_flags[index]);
    }

    static Key RealKey(KeyEventArgs e) =>
        e.Key == Key.System ? e.SystemKey :
        e.Key == Key.ImeProcessed ? e.ImeProcessedKey :
        e.Key == Key.DeadCharProcessed ? e.DeadCharProcessedKey :
        e.Key;

    static int ModifiersFromKeyboard(ModifierKeys keys)
    {
        int modifiers = 0;
        if ((keys & ModifierKeys.Control) == ModifierKeys.Control) modifiers |= ModControl;
        if ((keys & ModifierKeys.Alt) == ModifierKeys.Alt) modifiers |= ModAlt;
        if ((keys & ModifierKeys.Shift) == ModifierKeys.Shift) modifiers |= ModShift;
        return modifiers;
    }

    static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    static bool IsBareFunctionKey(Key key) =>
        key >= Key.F1 && key <= Key.F12 && key != Key.F8;

    static string FormatHotkey(int modifiers, Key key)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        parts.Add(KeyToText(key));
        return string.Join("+", parts);
    }

    static string KeyToText(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
            return ((int)key - (int)Key.D0).ToString();
        return key.ToString();
    }

    static bool TryParseHotkey(string hotkey, out int modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;

        foreach (string part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                default:
                    if (key != Key.None || !TryParseKeyText(part, out key))
                        return false;
                    break;
            }
        }

        return key != Key.None;
    }

    static bool TryParseKeyText(string text, out Key key)
    {
        key = Key.None;
        if (text.Length == 1)
        {
            char c = char.ToUpperInvariant(text[0]);
            if (c >= 'A' && c <= 'Z')
            {
                key = (Key)((int)Key.A + c - 'A');
                return true;
            }

            if (c >= '0' && c <= '9')
            {
                key = (Key)((int)Key.D0 + c - '0');
                return true;
            }
        }

        if (!Enum.TryParse(text, true, out Key parsed) || IsModifierKey(parsed))
            return false;

        key = parsed;
        return true;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int hotkeyId = wParam.ToInt32();
            if (hotkeyId == HotkeyApply)
                _ = ApplyAllAsync();
            else if (_flagHotkeys.TryGetValue(hotkeyId, out int index))
                ToggleFlagAt(index);
            handled = true;
        }

        return IntPtr.Zero;
    }

    void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record AvailableFlagRow(string Name, string Category, string Type);

public sealed class ModifiedFlagRow : INotifyPropertyChanged
{
    readonly FlagEntry _flag;

    public ModifiedFlagRow(FlagEntry flag) => _flag = flag;

    public string Name => _flag.Name;
    public string Value => _flag.Value;
    public string Type => _flag.Type;
    public string Enabled => _flag.Enabled ? "Yes" : "No";
    public string DefaultValue => _flag.DefaultValue ?? "-";
    public string Hotkey => string.IsNullOrWhiteSpace(_flag.Hotkey) ? "-" : _flag.Hotkey;

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
