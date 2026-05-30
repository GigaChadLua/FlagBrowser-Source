using System.Windows;
using FlagInjector;

namespace FlagInjectorWpf;

public partial class SettingsWindow : Window
{
    readonly FeatureSettings _settings;
    readonly Action<FeatureSettings> _onSave;

    public SettingsWindow(FeatureSettings settings, Action<FeatureSettings> onSave)
    {
        InitializeComponent();
        _settings = settings;
        _onSave = onSave;
        LoadValues();
    }

    void LoadValues()
    {
        bool elevated = FeatureEngine.IsSelfElevated();
        ElevationText.Text = elevated
            ? "Running as Administrator. Memory writes should work."
            : "Not running as Administrator. Writes may fail if Roblox is elevated.";
        ElevationText.Foreground = (System.Windows.Media.Brush)FindResource(elevated ? "Green" : "Muted");

        SafeModeBox.IsChecked = _settings.SafeMode;
        ReApplyBox.IsChecked = _settings.ReApplyEnabled;
        ReApplyIntervalBox.Text = _settings.ReApplyIntervalMs.ToString();
        RandomReApplyBox.IsChecked = _settings.RandomReApply;
        RandomMinBox.Text = _settings.RandomMinMs.ToString();
        RandomMaxBox.Text = _settings.RandomMaxMs.ToString();
        TimingAttackBox.IsChecked = _settings.TimingAttack;
        TimingDelayBox.Text = _settings.TimingDelayMs.ToString();
        OffsetlessBox.IsChecked = _settings.OffsetlessEnabled;
        DiskFallbackBox.IsChecked = _settings.DiskFallbackEnabled;
        StealthBox.IsChecked = _settings.StealthMode;
        ShuffleBox.IsChecked = _settings.ShuffleEnabled;
        BatchSizeBox.Text = _settings.BatchSize.ToString();
        BatchDelayBox.Text = _settings.BatchDelayMs.ToString();
    }

    void SaveValues()
    {
        _settings.SafeMode = SafeModeBox.IsChecked == true;
        _settings.ReApplyEnabled = ReApplyBox.IsChecked == true;
        _settings.ReApplyIntervalMs = ReadInt(ReApplyIntervalBox.Text, 500, 60000, _settings.ReApplyIntervalMs);
        _settings.RandomReApply = RandomReApplyBox.IsChecked == true;
        _settings.RandomMinMs = ReadInt(RandomMinBox.Text, 1000, 30000, _settings.RandomMinMs);
        _settings.RandomMaxMs = ReadInt(RandomMaxBox.Text, 1000, 60000, _settings.RandomMaxMs);
        if (_settings.RandomMaxMs < _settings.RandomMinMs)
            _settings.RandomMaxMs = _settings.RandomMinMs;
        _settings.TimingAttack = TimingAttackBox.IsChecked == true;
        _settings.TimingDelayMs = ReadInt(TimingDelayBox.Text, 0, 5000, _settings.TimingDelayMs);
        _settings.OffsetlessEnabled = OffsetlessBox.IsChecked == true;
        _settings.DiskFallbackEnabled = DiskFallbackBox.IsChecked == true;
        _settings.StealthMode = StealthBox.IsChecked == true;
        _settings.ShuffleEnabled = ShuffleBox.IsChecked == true;
        _settings.BatchSize = ReadInt(BatchSizeBox.Text, 1, 200, _settings.BatchSize);
        _settings.BatchDelayMs = ReadInt(BatchDelayBox.Text, 0, 500, _settings.BatchDelayMs);
        _settings.Save();
    }

    static int ReadInt(string text, int min, int max, int fallback)
    {
        if (!int.TryParse(text.Trim(), out int value))
            value = fallback;
        return Math.Max(min, Math.Min(max, value));
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveValues();
        _onSave(_settings);
        DialogResult = true;
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
