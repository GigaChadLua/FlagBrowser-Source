using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FlagInjector;
using Microsoft.Win32;

namespace FlagInjectorWpf;

public partial class MainWindow : Window
{
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

    readonly List<string> _allAvailableNames = new();
    readonly List<FlagEntry> _flags = new();

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

        Loaded += async (_, _) =>
        {
            ApplyOffsetSettings();
            LoadDefaults();
            await LoadOffsetsAsync();
            StartRobloxMonitor();
        };
        Closed += (_, _) =>
        {
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
        flag.Enabled = !flag.Enabled;

        if (_engine.IsAttached)
        {
            if (flag.Enabled)
                _engine.ApplyOne(flag, _loader.Offsets, _scanner, _features.OffsetlessEnabled, _features.DiskFallbackEnabled);
            else
                _engine.UninjectOne(flag.Name);
        }

        RefreshModified();
        SelectModified(flag.Name);
        SetStatus($"{flag.Name}: {(flag.Enabled ? "Enabled" : "Disabled")}");
    }

    void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var flag = SelectedFlag();
        if (flag is null) return;
        _flags.Remove(flag);
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
        if (flag?.DefaultValue is null) return;
        flag.Update(flag.DefaultValue);
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
        RefreshModified();
        SelectModified(flag.Name);
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
        RefreshAvailable();
        RefreshModified();
        SetStatus($"Profile 'Default' loaded ({_flags.Count} flags).");
    }

    void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON Files|*.json", FileName = "flags_export.json" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, FlagParser.ToJson(_flags));
        SetStatus($"Exported {_flags.Count} flags.");
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
