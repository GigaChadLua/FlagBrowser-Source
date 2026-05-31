using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace FlagInjector;
public sealed class MainForm : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, int mod, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    const int WM_HOTKEY = 0x0312;
    const int HK_APPLY  = 1;
    const int HK_FLAG_BASE = 1000;
    const int MOD_ALT      = 0x0001;
    const int MOD_CONTROL  = 0x0002;
    const int MOD_SHIFT    = 0x0004;
    const int MOD_NOREPEAT = 0x4000;
    static readonly string APP_DIR            = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlagInjector");
    const string DefaultUrl1 = "https://imtheo.lol/Offsets/FFlags.cs";
    const string DefaultUrl2 = "https://npdrlaufeimrkvdnjijl.supabase.co/functions/v1/get-offsets";
    const string ExtraOffsetsUrl = "https://raw.githubusercontent.com/soulukr78/BestRobloxOffsets/refs/heads/main/BestRobloxOffsets";
    readonly InjectionEngine   _engine   = new();
    readonly OffsetLoader      _loader   = new();
    readonly OffsetlessScanner _scanner  = new();
    AppSettings                _appCfg   = AppSettings.Load();
    FeatureSettings            _feat     = FeatureSettings.Load();
    FeatureEngine?             _featEng;
    CancellationTokenSource?   _timingCts;
    List<FlagEntry> _flags    = new();
    int             _selMod   = -1;
    string          _selAvail = "";
    bool            _autoAttach = true;
    bool            _showPresets = true;
    readonly Dictionary<int, int> _flagHotkeys = new();
    Label        lblOffsets   = null!;
    TextBox      txtSearchAvail = null!;
    ListBox      lstAvail       = null!;
    CheckBox     chkShowPresets = null!;
    TextBox      txtFlagValue   = null!;
    Button       btnAdd         = null!;
    TextBox      txtSearchMod  = null!;
    ListView     lvMod         = null!;
    TextBox      txtUpdateVal  = null!;
    Button       btnUpdate     = null!;
    Button       btnToggle     = null!;
    Button       btnRemove     = null!;
    TextBox      txtDefaultVal = null!;
    Button       btnSetDef     = null!;
    Button       btnResetDef   = null!;
    Button       btnClearDef   = null!;
    TextBox      txtHotkey     = null!;
    Button       btnClearHotkey = null!;
    Label        lblFlagInfo   = null!;
    CheckBox             chkAutoAttach = null!;
    ToolStripStatusLabel tsslStatus    = null!;
    ToolStripStatusLabel tsslRoblox    = null!;
    ToolStripStatusLabel tsslDefaults  = null!;
    SplitContainer?      _split;
    public MainForm()
    {
        Text          = "FFlag Injector";
        Size          = new Size(_appCfg.WindowWidth, _appCfg.WindowHeight);
        MinimumSize   = new Size(820, 560);
        Font          = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        FormClosing  += OnFormClosing;
        Load         += OnLoad;
        Directory.CreateDirectory(APP_DIR);
        _engine.OnAttached += (pid, b) =>
        {
            string msg = $"● Attached  PID {pid}  |  Base 0x{b:X}";
            bool robloxElevated = FeatureEngine.IsProcessElevated(pid);
            bool selfElevated   = FeatureEngine.IsSelfElevated();
            if (robloxElevated && !selfElevated)
            {
                msg += "  ⚠ Roblox is Admin — restart injector as Admin!";
                SetRobloxStatus(msg, Color.DarkOrange);
                ShowElevationWarning();
            }
            else
            {
                SetRobloxStatus(msg, Color.Green);
            }
        };
        _engine.OnDetachCleanup += () => _scanner.ClearCache();  
        _engine.OnDetached += () => SetRobloxStatus("● Not attached", Color.Crimson);
        BuildUI();
        ApplySettings();
        ApplyTheme();
        var tmr = new System.Windows.Forms.Timer { Interval = 2000 };
        tmr.Tick += MonitorRoblox;
        tmr.Start();
    }
    void ShowElevationWarning()
    {
        BeginInvoke(() =>
        {
            MessageBox.Show(
                "⚠ Roblox is running as Administrator but this injector is NOT.\n\n" +
                "Memory writes will fail silently.\n\n" +
                "Solutions:\n" +
                "  • Restart the injector as Administrator\n" +
                "  • Don't run Roblox as Administrator\n" +
                "  • Enable 'Disk Backup' in Settings",
                "Elevation Mismatch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        });
    }
    void OnLoad(object? s, EventArgs e)
    {
        RegisterHotKey(Handle, HK_APPLY, 0, (int)Keys.F8);
        RegisterFlagHotkeys();
        if (_split is not null)
        {
            _split.Panel1MinSize    = 200;
            _split.Panel2MinSize    = 340;
            _split.SplitterDistance = Math.Max(200, Math.Min(_appCfg.SplitterPos, Width - 340));
        }
        if (!FeatureEngine.IsSelfElevated())
            Text = "FFlag Injector  [not elevated — some writes may fail]";
        else
            Text = "FFlag Injector  [Administrator]";
        _ = LoadOffsetsAsync();
        LoadDefaultValues();
        InitFeatEngine();
        _ = CheckForUpdatesAsync(userInitiated: false);
    }
    void OnFormClosing(object? s, FormClosingEventArgs e)
    {
        UnregisterFlagHotkeys();
        UnregisterHotKey(Handle, HK_APPLY);
        _featEng?.Dispose();
        _timingCts?.Cancel();
        _engine.Dispose();
        _appCfg.WindowWidth  = Width;
        _appCfg.WindowHeight = Height;
        if (_split is not null) _appCfg.SplitterPos = _split.SplitterDistance;
        _appCfg.ShowPresets = _showPresets;
        _appCfg.Save();
    }
    void BuildUI()
    {
        var ss = new StatusStrip { SizingGrip = false };
        tsslRoblox   = new ToolStripStatusLabel("● Not attached") { ForeColor = Color.Crimson };
        tsslDefaults = new ToolStripStatusLabel("defaults: not loaded") { ForeColor = Color.Gray };
        tsslStatus   = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        ss.Items.AddRange(new ToolStripItem[] { tsslRoblox, new ToolStripSeparator(), tsslDefaults, new ToolStripSeparator(), tsslStatus });
        var pnlTop = BuildTopPanel();
        var pnlBot = BuildBottomPanel();
        var split = new SplitContainer { Dock = DockStyle.Fill };
        _split = split;
        BuildLeftPanel(split.Panel1);
        BuildRightPanel(split.Panel2);
        ss.Dock     = DockStyle.Bottom;
        pnlBot.Dock = DockStyle.Bottom;
        pnlTop.Dock = DockStyle.Top;
        split.Dock  = DockStyle.Fill;
        Controls.Add(split);
        Controls.Add(pnlTop);
        Controls.Add(pnlBot);
        Controls.Add(ss);
    }
    Panel BuildTopPanel()
    {
        var pnl = new Panel { Height = 64, BackColor = Color.FromArgb(245, 245, 245) };
        var title = new Label { Text = "Offset Data", Left = 8, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        var subtitle = new Label { Text = "Managed automatically", Left = 8, Top = 28, AutoSize = true, ForeColor = Color.Gray };
        var btnLoad = new Button { Text = "Load", Width = 80, Height = 26, Top = 18, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        pnl.Resize += (_, _) => btnLoad.Left = pnl.Width - btnLoad.Width - 8;
        btnLoad.Click += async (_, _) => await LoadOffsetsAsync();
        lblOffsets = new Label { Left = 190, Top = 29, AutoSize = true, ForeColor = Color.Gray };
        pnl.Controls.AddRange(new Control[] { title, subtitle, btnLoad, lblOffsets });
        return pnl;
    }
    Panel BuildBottomPanel()
    {
        var pnl = new Panel { Height = 46 };
        int x   = 6;
        Btn(pnl, "➕ Add Flag", ref x, DoAdicionar, width: 105);
        Btn(pnl, "Export JSON", ref x, DoExport,    width: 95);
        Btn(pnl, "Remove All",  ref x, DoRemoveAll, width: 85);
        Btn(pnl, "📂 Defaults", ref x, BrowseDefaultValues, width: 90);
        Btn(pnl, "Restore All",  ref x, DoUninject,  width: 95);
        x += 8;
        var btnApply = new Button { Text = "▶ Apply All  [F8]", Left = x, Top = 8, Width = 128, Height = 30 };
        btnApply.BackColor = Color.FromArgb(34, 110, 34);
        btnApply.ForeColor = Color.White;
        btnApply.FlatStyle = FlatStyle.Flat;
        btnApply.Click    += (_, _) => TriggerApply();
        pnl.Controls.Add(btnApply); x += 134;
        chkAutoAttach = new CheckBox { Text = "Auto-attach", Left = x, Top = 13, AutoSize = true, Checked = true };
        chkAutoAttach.CheckedChanged += (_, _) => _autoAttach = chkAutoAttach.Checked;
        pnl.Controls.Add(chkAutoAttach); x += 100;
        var btnSettings = new Button { Text = "⚙ Settings", Left = x, Top = 8, Width = 88, Height = 30 };
        btnSettings.Click += (_, _) => new SettingsForm(_feat, _appCfg, OnSettingsSaved).ShowDialog(this);
        pnl.Controls.Add(btnSettings); x += 92;
        var btnCheckUpdate = new Button { Text = "Update", Left = x, Top = 8, Width = 70, Height = 30 };
        btnCheckUpdate.Click += async (_, _) => await CheckForUpdatesAsync(userInitiated: true);
        pnl.Controls.Add(btnCheckUpdate);
        return pnl;
    }
    void BuildLeftPanel(SplitterPanel p)
    {
        var lbl = new Label { Text = "Available Flags:", Dock = DockStyle.Top, Height = 20, Padding = new Padding(2) };
        txtSearchAvail = new TextBox { Dock = DockStyle.Top, PlaceholderText = "🔍 Search available flags..." };
        txtSearchAvail.TextChanged += (_, _) => RefreshAvail();
        chkShowPresets = new CheckBox
        {
            Text = "Show presets",
            Dock = DockStyle.Top,
            Height = 22,
            Checked = _showPresets,
            Padding = new Padding(2, 0, 0, 0)
        };
        chkShowPresets.CheckedChanged += (_, _) => { _showPresets = chkShowPresets.Checked; RefreshAvail(); };
        lstAvail = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        lstAvail.SelectedIndexChanged += (_, _) =>
        {
            _selAvail      = lstAvail.SelectedItem?.ToString() ?? "";
            btnAdd.Enabled = _selAvail.Length > 0;
        };
        lstAvail.DoubleClick += (_, _) => { if (txtFlagValue.Text.Trim().Length > 0) DoAddFlag(); };
        var pnlAdd = new Panel { Dock = DockStyle.Bottom, Height = 38 };
        txtFlagValue = new TextBox { Left = 4, Top = 8, Width = 190, PlaceholderText = "Value (true, 100, 3.14)" };
        btnAdd = new Button { Left = 198, Top = 6, Width = 76, Height = 26, Text = "Add  →", Enabled = false };
        btnAdd.Click += (_, _) => DoAddFlag();
        pnlAdd.Controls.AddRange(new Control[] { txtFlagValue, btnAdd });
        p.Controls.Add(lstAvail);
        p.Controls.Add(pnlAdd);
        p.Controls.Add(chkShowPresets);
        p.Controls.Add(txtSearchAvail);
        p.Controls.Add(lbl);
    }
    void BuildRightPanel(SplitterPanel p)
    {
        var lbl = new Label { Text = "Modified Flags:", Dock = DockStyle.Top, Height = 20, Padding = new Padding(2) };
        txtSearchMod = new TextBox { Dock = DockStyle.Top, PlaceholderText = "🔍 Search modified flags..." };
        txtSearchMod.TextChanged += (_, _) => RefreshMod();
        lvMod = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
        lvMod.OwnerDraw = true;
        lvMod.DrawColumnHeader += DrawModColumnHeader;
        lvMod.DrawSubItem += DrawModSubItem;
        lvMod.DrawItem += (_, _) => { };
        lvMod.Columns.Add("Flag Name", 210);
        lvMod.Columns.Add("Value",      90);
        lvMod.Columns.Add("Type",       55);
        lvMod.Columns.Add("En",         30);
        lvMod.Columns.Add("Default",    90);
        lvMod.Columns.Add("Original",   90);
        lvMod.Columns.Add("Hotkey",     90);
        lvMod.SelectedIndexChanged += OnModSelect;
        lvMod.DoubleClick          += (_, _) => { if (txtUpdateVal.Text.Trim().Length > 0) DoUpdate(); };
        var pnlAct = new Panel { Dock = DockStyle.Bottom, Height = 106, Padding = new Padding(4, 2, 4, 2) };
        txtUpdateVal = new TextBox { Left = 4,   Top = 4,  Width = 158, PlaceholderText = "New value..." };
        btnUpdate    = new Button  { Left = 166, Top = 2,  Width = 66, Height = 26, Text = "Update",  Enabled = false };
        btnToggle    = new Button  { Left = 236, Top = 2,  Width = 66, Height = 26, Text = "On/Off",  Enabled = false };
        btnRemove    = new Button  { Left = 306, Top = 2,  Width = 66, Height = 26, Text = "Remove",  Enabled = false };
        btnUpdate.Click += (_, _) => DoUpdate();
        btnToggle.Click += (_, _) => DoToggle();
        btnRemove.Click += (_, _) => DoRemove();
        var lblDef = new Label  { Left = 4,   Top = 36, AutoSize = true, Text = "Default:", ForeColor = Color.Gray };
        txtDefaultVal = new TextBox { Left = 58, Top = 33, Width = 106, PlaceholderText = "Default value..." };
        btnSetDef   = new Button { Left = 168, Top = 31, Width = 88, Height = 26, Text = "Set Default",   Enabled = false };
        btnResetDef = new Button { Left = 260, Top = 31, Width = 96, Height = 26, Text = "↺ Reset → Def", Enabled = false };
        btnClearDef = new Button { Left = 360, Top = 31, Width = 78, Height = 26, Text = "Clear Def",     Enabled = false };
        btnSetDef.Click   += (_, _) => DoSetDefault();
        btnResetDef.Click += (_, _) => DoResetToDefault();
        btnClearDef.Click += (_, _) => DoClearDefault();
        var lblHotkey = new Label { Left = 4, Top = 64, AutoSize = true, Text = "Hotkey:", ForeColor = Color.Gray };
        txtHotkey = new TextBox { Left = 58, Top = 61, Width = 154, ReadOnly = true, PlaceholderText = "Focus and press keys..." };
        txtHotkey.KeyDown += CaptureHotkey;
        btnClearHotkey = new Button { Left = 218, Top = 59, Width = 86, Height = 26, Text = "Clear Key", Enabled = false };
        btnClearHotkey.Click += (_, _) => ClearSelectedHotkey();
        lblFlagInfo = new Label { Left = 4, Top = 88, AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 7.5f) };
        pnlAct.Controls.AddRange(new Control[] {
            txtUpdateVal, btnUpdate, btnToggle, btnRemove,
            lblDef, txtDefaultVal, btnSetDef, btnResetDef, btnClearDef,
            lblHotkey, txtHotkey, btnClearHotkey,
            lblFlagInfo
        });
        p.Controls.Add(lvMod);
        p.Controls.Add(pnlAct);
        p.Controls.Add(txtSearchMod);
        p.Controls.Add(lbl);
    }
    static void Btn(Panel p, string text, ref int x, Action action, int width = 72)
    {
        var b = new Button { Text = text, Left = x, Top = 8, Width = width, Height = 30 };
        b.Click += (_, _) => action();
        p.Controls.Add(b);
        x += width + 4;
    }
    void ApplySettings()
    {
        _showPresets       = _appCfg.ShowPresets;
        if (chkShowPresets is not null) chkShowPresets.Checked = _showPresets;
    }
    void SaveAppSettings()
    {
        _appCfg.ShowPresets   = _showPresets;
        _appCfg.Save();
    }
    void OnSettingsSaved(FeatureSettings cfg)
    {
        _feat = cfg;
        InitFeatEngine();
        ApplyTheme();
        SetStatus("Settings saved.");
    }
    void ApplyTheme()
    {
        bool dark = _appCfg.DarkMode;
        Color back  = dark ? Color.FromArgb(12, 12, 12) : SystemColors.Control;
        Color panel = dark ? Color.FromArgb(22, 22, 22) : SystemColors.Control;
        Color input = dark ? Color.FromArgb(30, 30, 30) : SystemColors.Window;
        Color text  = dark ? Color.FromArgb(235, 235, 235) : SystemColors.ControlText;
        Color muted = dark ? Color.FromArgb(155, 155, 155) : Color.Gray;

        BackColor = back;
        ForeColor = text;
        ApplyThemeRecursive(this, dark, panel, input, text, muted);

        if ((tsslRoblox.Text ?? "").Contains("Not attached", StringComparison.OrdinalIgnoreCase))
            tsslRoblox.ForeColor = dark ? Color.FromArgb(255, 106, 106) : Color.Crimson;
        if (!FlagDefaults.Instance.IsLoaded)
            tsslDefaults.ForeColor = muted;
        RefreshMod();
    }
    void ApplyThemeRecursive(Control parent, bool dark, Color panel, Color input, Color text, Color muted)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case Panel p:
                    p.BackColor = panel;
                    p.ForeColor = text;
                    break;
                case SplitContainer s:
                    s.BackColor = dark ? Color.FromArgb(12, 12, 12) : SystemColors.Control;
                    s.ForeColor = text;
                    break;
                case TextBox tb:
                    tb.BackColor = input;
                    tb.ForeColor = text;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ListBox lb:
                    lb.BackColor = input;
                    lb.ForeColor = text;
                    break;
                case ListView lv:
                    lv.BackColor = input;
                    lv.ForeColor = text;
                    lv.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                    break;
                case ComboBox cb:
                    cb.BackColor = input;
                    cb.ForeColor = text;
                    break;
                case Label l:
                    l.BackColor = Color.Transparent;
                    if (l.ForeColor == Color.Gray || l.ForeColor == Color.DarkGray || l.ForeColor == SystemColors.ControlText)
                        l.ForeColor = dark ? muted : SystemColors.ControlText;
                    break;
                case CheckBox chk:
                    chk.BackColor = Color.Transparent;
                    chk.ForeColor = text;
                    break;
                case RadioButton rb:
                    rb.BackColor = Color.Transparent;
                    rb.ForeColor = text;
                    break;
                case Button b:
                    b.ForeColor = dark ? text : SystemColors.ControlText;
                    b.BackColor = dark ? Color.FromArgb(38, 38, 38) : SystemColors.Control;
                    if (b.Text.Contains("Apply All", StringComparison.OrdinalIgnoreCase))
                    {
                        b.BackColor = Color.FromArgb(34, 110, 34);
                        b.ForeColor = Color.White;
                    }
                    b.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                    b.UseVisualStyleBackColor = false;
                    if (dark)
                    {
                        b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 48, 48);
                        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(28, 28, 28);
                        b.FlatAppearance.BorderSize = 1;
                    }
                    break;
                case StatusStrip ss:
                    ss.BackColor = dark ? Color.FromArgb(16, 16, 16) : SystemColors.Control;
                    ss.ForeColor = text;
                    foreach (ToolStripItem item in ss.Items)
                        if (item is ToolStripStatusLabel { Spring: true })
                            item.ForeColor = text;
                    break;
            }
            if (c.HasChildren)
                ApplyThemeRecursive(c, dark, panel, input, text, muted);
        }
    }
    async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (!Updater.IsManifestConfigured(_appCfg.UpdateManifestUrl))
        {
            if (userInitiated)
                MessageBox.Show(
                    "Update manifest URL is not configured yet.",
                    "Updater",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            return;
        }
        if (!userInitiated && !_appCfg.AutoCheckUpdates) return;

        SetStatus("Checking updates...");
        var result = await Updater.CheckAsync(_appCfg.UpdateManifestUrl);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            SetStatus("Update check failed.");
            if (userInitiated)
                MessageBox.Show($"Failed to check updates:\n{result.Error}", "Updater", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!result.IsUpdateAvailable || result.Manifest is null)
        {
            SetStatus(userInitiated ? $"You are up to date ({result.CurrentVersion})." : "Ready");
            return;
        }

        string notes = string.IsNullOrWhiteSpace(result.Manifest.Notes) ? "" : $"\n\nNotes:\n{result.Manifest.Notes}";
        var ask = MessageBox.Show(
            $"New version available: {result.LatestVersion}\nCurrent: {result.CurrentVersion}{notes}\n\nOpen download now?",
            "Update Available",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (ask != DialogResult.Yes)
        {
            SetStatus("Update postponed.");
            return;
        }

        try
        {
            Updater.OpenDownload(result.Manifest);
            SetStatus($"Update available: {result.LatestVersion}");
        }
        catch (Exception ex)
        {
            SetStatus("Update failed.");
            MessageBox.Show($"Could not open update:\n{ex.Message}", "Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    void InitFeatEngine()
    {
        _featEng?.Dispose();
        _featEng = new FeatureEngine(_feat, ApplyAllAsync, () => _engine.IsAttached, () => _engine.Pid);
        if (_feat.ReApplyEnabled) _featEng.SetReApply(true);
        if (_feat.RandomReApply)  _featEng.SetRandomReApply(true);
    }
    void MonitorRoblox(object? _, EventArgs __)
    {
        var proc = Process.GetProcessesByName(Obf.ProcName).FirstOrDefault();
        if (proc is not null && proc.Id != _engine.Pid)
        {
            nint baseAddr = 0;
            try { baseAddr = (nint)proc.MainModule!.BaseAddress; }
            catch {  }
            if (baseAddr == 0)
            {
                SetRobloxStatus($"● Roblox (PID {proc.Id}) — waiting for base address...", Color.DarkOrange);
                return;
            }
            _engine.TryAttach(proc.Id, baseAddr);
            if (_autoAttach && _flags.Count > 0)
            {
                bool recording = _featEng?.CheckEnv() ?? false;
                if (!recording)
                {
                    if (_feat.TimingAttack)
                    {
                        _timingCts?.Cancel();
                        _timingCts = new CancellationTokenSource();
                        _ = FeatureEngine.TimingAttackApply(ApplyAllAsync, _feat.TimingDelayMs, _timingCts.Token);
                        SetStatus($"[BETA] Timing attack — applying in {_feat.TimingDelayMs}ms...");
                    }
                    else _ = ApplyWhenReadyAsync();
                }
                else SetStatus("[Stealth] Recording detected — skipped.");
            }
        }
        else if (proc is null && _engine.IsAttached)
        {
            _engine.Detach();
        }
    }
    async Task LoadOffsetsAsync()
    {
        SetStatus("Fetching offsets...");
        var urls = new[] { DefaultUrl1, DefaultUrl2, ExtraOffsetsUrl }
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var (count, errors) = await _loader.LoadUrlsAsync(urls);
        SaveAppSettings();
        SyncScannerFromLoader();
        if (count == 0)
        {
            SetStatus($"FAIL: No offsets. {string.Join(" | ", errors)}");
            return;
        }
        string e = errors.Count > 0 ? $"  ({errors.Count} source failed)" : "";
        lblOffsets.Text = $"{count} offsets{e}";
        SetStatus($"✔ {count} offsets loaded");
        RefreshAvail();
    }
    void BrowseDefaultValues()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Load Default Values (.hpp)",
            Filter = "HPP files|*.hpp|All files|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            LoadDefaultValuesFromFile(dlg.FileName);
    }
    void SyncScannerFromLoader()
    {
        nint flogDb = _loader.BaseRva;
        if (flogDb == 0)
            _scanner.SyncRva(_loader.Offsets);  
        else
            _scanner.BaseRva = flogDb;
        _scanner.ToFlag  = _loader.StructToFlag;
        _scanner.ToValue = _loader.StructToValue;
        string flogHex = _scanner.BaseRva != 0
            ? $"  |  FFlagList 0x{_scanner.BaseRva:X}"
            : "  |  FFlagList: NOT FOUND (offsetless disabled)";
        SetStatus($"✔ {_loader.Offsets.Count} offsets{flogHex}");
    }
    void LoadDefaultValues()
    {
        string path = _appCfg.DefaultValuesPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            path = FlagDefaults.DefaultFilePath;
        if (!File.Exists(path)) return; 
        var (count, error) = FlagDefaults.Instance.LoadFile(path);
        if (error is not null)
        {
            tsslDefaults.Text      = $"defaults: error — {error}";
            tsslDefaults.ForeColor = StatusColor(error: true);
        }
        else
        {
            tsslDefaults.Text      = $"defaults: {count:N0} flags";
            tsslDefaults.ForeColor = StatusColor(success: true);
        }
    }
    public void LoadDefaultValuesFromFile(string path)
    {
        var (count, error) = FlagDefaults.Instance.LoadFile(path);
        if (error is not null)
        {
            tsslDefaults.Text      = $"defaults: error — {error}";
            tsslDefaults.ForeColor = StatusColor(error: true);
            return;
        }
        _appCfg.DefaultValuesPath = path;
        _appCfg.Save();
        try
        {
            string dest = FlagDefaults.DefaultFilePath;
            if (!File.Exists(dest) || path != dest)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(path, dest, overwrite: true);
            }
        }
        catch {  }
        tsslDefaults.Text      = $"defaults: {count:N0} flags loaded";
        tsslDefaults.ForeColor = StatusColor(success: true);
        bool any = false;
        foreach (var f in _flags)
        {
            if (f.DefaultValue is null)
            {
                var def = FlagDefaults.Instance.Get(f.Name);
                if (def is not null) { f.DefaultValue = def; any = true; }
            }
        }
        if (any) RefreshMod();
        SetStatus($"✔ {count:N0} default values loaded from {Path.GetFileName(path)}");
    }
    void RefreshAvail()
    {
        var filter = txtSearchAvail.Text;
        var inUse  = _flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lstAvail.BeginUpdate();
        lstAvail.Items.Clear();
        foreach (var name in _loader.SortedNames)
        {
            if (inUse.Contains(name)) continue;
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            lstAvail.Items.Add(name);
        }
        if (_showPresets)
        {
            var alreadyListed = new HashSet<string>(lstAvail.Items.Cast<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var name in PresetFlags.AllFlagNames)
            {
                if (inUse.Contains(name) || alreadyListed.Contains(name)) continue;
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                lstAvail.Items.Add(name + "  [preset]");
            }
        }
        lstAvail.EndUpdate();
    }
    void RefreshMod()
    {
        var filter = txtSearchMod.Text;
        lvMod.BeginUpdate();
        lvMod.Items.Clear();
        foreach (var f in _flags)
        {
            if (filter.Length > 0 && !f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var item = new ListViewItem(f.Name);
            item.SubItems.Add(f.Value);
            item.SubItems.Add(f.Type);
            item.SubItems.Add(f.Enabled ? "✓" : "✗");
            item.SubItems.Add(f.DefaultValue ?? "-");
            item.SubItems.Add(f.OriginalValue);
            item.SubItems.Add(string.IsNullOrWhiteSpace(f.Hotkey) ? "-" : f.Hotkey);
            item.Tag = f;
            item.BackColor = ModifiedRowBackColor(f);
            item.ForeColor = ModifiedRowForeColor(f);
            if (f.DefaultValue is not null &&
                !f.Value.Equals(f.DefaultValue, StringComparison.OrdinalIgnoreCase))
                item.BackColor = ModifiedRowBackColor(f);
            lvMod.Items.Add(item);
        }
        lvMod.EndUpdate();
    }
    Color ModifiedRowBackColor(FlagEntry f)
    {
        if (!_appCfg.DarkMode)
        {
            if (f.DefaultValue is not null &&
                !f.Value.Equals(f.DefaultValue, StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(245, 255, 245);
            return SystemColors.Window;
        }

        if (!f.Enabled) return Color.FromArgb(24, 24, 24);
        if (f.DefaultValue is not null &&
            !f.Value.Equals(f.DefaultValue, StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(34, 43, 34);
        return Color.FromArgb(30, 30, 30);
    }
    Color ModifiedRowForeColor(FlagEntry f)
    {
        if (!f.Enabled) return _appCfg.DarkMode ? Color.FromArgb(125, 125, 125) : Color.Gray;
        return _appCfg.DarkMode ? Color.FromArgb(235, 235, 235) : SystemColors.ControlText;
    }
    void DrawModColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        if (!_appCfg.DarkMode)
        {
            e.DrawDefault = true;
            return;
        }

        using var bg = new SolidBrush(Color.FromArgb(22, 22, 22));
        using var fg = new SolidBrush(Color.FromArgb(215, 215, 215));
        using var border = new Pen(Color.FromArgb(55, 55, 55));
        e.Graphics.FillRectangle(bg, e.Bounds);
        e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        TextRenderer.DrawText(
            e.Graphics,
            e.Header?.Text ?? "",
            lvMod.Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
            Color.FromArgb(215, 215, 215),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
    void DrawModSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (!_appCfg.DarkMode)
        {
            e.DrawDefault = true;
            return;
        }

        var f = e.Item?.Tag as FlagEntry;
        Color bgColor = f is null ? Color.FromArgb(30, 30, 30) : ModifiedRowBackColor(f);
        Color fgColor = f is null ? Color.FromArgb(235, 235, 235) : ModifiedRowForeColor(f);
        if (e.Item?.Selected == true)
        {
            bgColor = Color.FromArgb(55, 55, 55);
            fgColor = Color.White;
        }

        using var bg = new SolidBrush(bgColor);
        using var grid = new Pen(Color.FromArgb(48, 48, 48));
        e.Graphics.FillRectangle(bg, e.Bounds);
        e.Graphics.DrawRectangle(grid, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        TextRenderer.DrawText(
            e.Graphics,
            e.SubItem?.Text ?? "",
            lvMod.Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
            fgColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
    Color StatusColor(bool success = false, bool error = false)
    {
        if (success) return _appCfg.DarkMode ? Color.FromArgb(92, 214, 150) : Color.DarkGreen;
        if (error) return _appCfg.DarkMode ? Color.FromArgb(255, 106, 106) : Color.DarkRed;
        return _appCfg.DarkMode ? Color.FromArgb(155, 155, 155) : Color.Gray;
    }
    void DoAddFlag()
    {
        string sel = _selAvail.Replace("  [preset]", "").Trim();
        string val = txtFlagValue.Text.Trim();
        if (sel.Length == 0 || val.Length == 0) return;
        if (_flags.Any(f => f.Name.Equals(sel, StringComparison.OrdinalIgnoreCase))) return;
        var entry = new FlagEntry(sel, val);
        entry.DefaultValue = FlagDefaults.Instance.Get(sel);
        _flags.Add(entry);
        _engine.WriteOne(entry, _loader.Offsets, _scanner, _feat.OffsetlessEnabled, _feat.DiskFallbackEnabled);
        RegisterFlagHotkeys();
        RefreshMod(); RefreshAvail();
        string defInfo = entry.DefaultValue is not null ? $"  (default: {entry.DefaultValue})" : "";
        SetStatus($"Added: {sel} = {val}  (type: {entry.Type}){defInfo}");
    }
    void OnModSelect(object? _, EventArgs __)
    {
        if (lvMod.SelectedItems.Count == 0) { _selMod = -1; ClearActions(); return; }
        var name = lvMod.SelectedItems[0].Text;
        _selMod  = _flags.FindIndex(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (_selMod < 0) { ClearActions(); return; }
        var f = _flags[_selMod];
        txtUpdateVal.Text  = f.Value;
        txtDefaultVal.Text = f.DefaultValue ?? "";
        txtHotkey.Text     = f.Hotkey ?? "";
        UpdateFlagInfoLabel();
        btnUpdate.Enabled = btnToggle.Enabled = btnRemove.Enabled = true;
        btnSetDef.Enabled = btnResetDef.Enabled = btnClearDef.Enabled = true;
        btnClearHotkey.Enabled = true;
    }
    void ClearActions()
    {
        txtUpdateVal.Text = txtDefaultVal.Text = txtHotkey.Text = lblFlagInfo.Text = "";
        btnUpdate.Enabled = btnToggle.Enabled = btnRemove.Enabled = false;
        btnSetDef.Enabled = btnResetDef.Enabled = btnClearDef.Enabled = false;
        btnClearHotkey.Enabled = false;
    }
    void UpdateFlagInfoLabel()
    {
        if (_selMod < 0 || _selMod >= _flags.Count) return;
        var f = _flags[_selMod];
        string hotkey = string.IsNullOrWhiteSpace(f.Hotkey) ? "(not set)" : f.Hotkey;
        lblFlagInfo.Text = $"Type: {f.Type}   Original: {f.OriginalValue}   Default: {f.DefaultValue ?? "(not set)"}   Hotkey: {hotkey}";
    }
    void DoUpdate()
    {
        if (_selMod < 0) return;
        var val = txtUpdateVal.Text.Trim();
        if (val.Length == 0) return;
        _flags[_selMod].Update(val);
        _engine.WriteOne(_flags[_selMod], _loader.Offsets, _scanner, _feat.OffsetlessEnabled, _feat.DiskFallbackEnabled);
        RefreshMod(); UpdateFlagInfoLabel();
        SetStatus($"Updated: {_flags[_selMod].Name} = {val}");
    }
    void DoToggle() => ToggleFlagAt(_selMod);
    void ToggleFlagAt(int index)
    {
        if (index < 0 || index >= _flags.Count) return;
        var f = _flags[index];
        f.Enabled = !f.Enabled;
        if (!f.Enabled && _engine.IsAttached)
        {
            if (!_engine.RestoreOne(f.Name))
            {
                string? restoreVal = f.DefaultValue ?? (f.OriginalValue.Length > 0 ? f.OriginalValue : null);
                if (restoreVal is not null)
                {
                    var temp = new FlagEntry(f.Name, restoreVal) { Type = f.Type };
                    _engine.WriteOne(temp, _loader.Offsets, _scanner, _feat.OffsetlessEnabled);
                }
            }
        }
        else if (f.Enabled && _engine.IsAttached)
        {
            _engine.WriteOne(f, _loader.Offsets, _scanner, _feat.OffsetlessEnabled, _feat.DiskFallbackEnabled);
        }
        RefreshMod();
        if (_selMod == index) UpdateFlagInfoLabel();
        SetStatus($"{f.Name}: {(f.Enabled ? "Enabled" : "Disabled")}");
    }
    void CaptureHotkey(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (_selMod < 0 || _selMod >= _flags.Count) return;

        Keys key = e.KeyCode;
        if (IsModifierKey(key)) return;

        int modifiers = ModifiersFromKeys(e.Modifiers);
        bool bareFunctionKey = IsBareFunctionKey(key);
        if (modifiers == 0 && !bareFunctionKey)
        {
            SetStatus("Use Ctrl/Alt/Shift, or F1-F12. F8 is reserved for Apply.");
            return;
        }
        if (modifiers == 0 && key == Keys.F8)
        {
            SetStatus("F8 is reserved for Apply.");
            return;
        }

        string hotkey = FormatHotkey(modifiers, key);
        int existing = _flags.FindIndex(f => string.Equals(f.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0 && existing != _selMod)
        {
            SetStatus($"Hotkey already used by {_flags[existing].Name}");
            return;
        }

        var flag = _flags[_selMod];
        flag.Hotkey = hotkey;
        txtHotkey.Text = hotkey;
        bool registered = RegisterFlagHotkeys();
        RefreshMod();
        UpdateFlagInfoLabel();
        SetStatus(registered ? $"Hotkey set: {flag.Name} -> {hotkey}" : $"Hotkey unavailable: {hotkey}");
    }
    void ClearSelectedHotkey()
    {
        if (_selMod < 0 || _selMod >= _flags.Count) return;
        var flag = _flags[_selMod];
        flag.Hotkey = "";
        txtHotkey.Text = "";
        RegisterFlagHotkeys();
        RefreshMod();
        UpdateFlagInfoLabel();
        SetStatus($"Hotkey cleared: {flag.Name}");
    }
    bool RegisterFlagHotkeys()
    {
        UnregisterFlagHotkeys();
        if (!IsHandleCreated) return true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool allRegistered = true;
        for (int i = 0; i < _flags.Count; i++)
        {
            string hotkey = (_flags[i].Hotkey ?? "").Trim();
            if (hotkey.Length == 0) continue;
            if (!TryParseHotkey(hotkey, out int modifiers, out int key))
            {
                allRegistered = false;
                continue;
            }

            string normalized = FormatHotkey(modifiers, (Keys)key);
            if (!seen.Add(normalized))
            {
                allRegistered = false;
                SetStatus($"Hotkey duplicated: {normalized}");
                continue;
            }

            int id = HK_FLAG_BASE + i;
            if (RegisterHotKey(Handle, id, modifiers | MOD_NOREPEAT, key))
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
        foreach (int id in _flagHotkeys.Keys.ToList())
            UnregisterHotKey(Handle, id);
        _flagHotkeys.Clear();
    }
    static int ModifiersFromKeys(Keys keys)
    {
        int modifiers = 0;
        if ((keys & Keys.Control) == Keys.Control) modifiers |= MOD_CONTROL;
        if ((keys & Keys.Alt) == Keys.Alt) modifiers |= MOD_ALT;
        if ((keys & Keys.Shift) == Keys.Shift) modifiers |= MOD_SHIFT;
        return modifiers;
    }
    static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin;
    static bool IsBareFunctionKey(Keys key) =>
        (int)key >= (int)Keys.F1 && (int)key <= (int)Keys.F12 && key != Keys.F8;
    static string FormatHotkey(int modifiers, Keys key)
    {
        var parts = new List<string>();
        if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(KeyToText(key));
        return string.Join("+", parts);
    }
    static string KeyToText(Keys key)
    {
        if ((int)key >= (int)Keys.D0 && (int)key <= (int)Keys.D9)
            return ((int)key - (int)Keys.D0).ToString();
        return key.ToString();
    }
    static bool TryParseHotkey(string hotkey, out int modifiers, out int key)
    {
        modifiers = 0;
        key = 0;
        Keys parsedKey = Keys.None;
        foreach (string part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                default:
                    if (parsedKey != Keys.None || !TryParseKeyText(part, out parsedKey))
                        return false;
                    break;
            }
        }
        if (parsedKey == Keys.None) return false;
        key = (int)parsedKey;
        return true;
    }
    static bool TryParseKeyText(string text, out Keys key)
    {
        key = Keys.None;
        if (text.Length == 1)
        {
            char c = char.ToUpperInvariant(text[0]);
            if (c >= 'A' && c <= 'Z')
            {
                key = (Keys)((int)Keys.A + c - 'A');
                return true;
            }
            if (c >= '0' && c <= '9')
            {
                key = (Keys)((int)Keys.D0 + c - '0');
                return true;
            }
        }
        if (!Enum.TryParse(text, true, out Keys parsed) || IsModifierKey(parsed))
            return false;
        key = parsed;
        return true;
    }
    void DoRemove()
    {
        if (_selMod < 0) return;
        string name = _flags[_selMod].Name;
        _flags.RemoveAt(_selMod);
        _selMod = -1; ClearActions();
        RegisterFlagHotkeys();
        RefreshMod(); RefreshAvail();
        SetStatus($"Removed: {name}");
    }
    void DoSetDefault()
    {
        if (_selMod < 0) return;
        string def = txtDefaultVal.Text.Trim();
        if (def.Length == 0) return;
        _flags[_selMod].DefaultValue = def;
        RefreshMod(); UpdateFlagInfoLabel();
        SetStatus($"Default set: {_flags[_selMod].Name} → {def}");
    }
    void DoResetToDefault()
    {
        if (_selMod < 0) return;
        var f   = _flags[_selMod];
        var val = f.DefaultValue ?? f.OriginalValue;
        f.Update(val);
        txtUpdateVal.Text = val;
        _engine.WriteOne(f, _loader.Offsets, _scanner, _feat.OffsetlessEnabled, _feat.DiskFallbackEnabled);
        RefreshMod();
        SetStatus($"Reset: {f.Name} = {val}");
    }
    void DoClearDefault()
    {
        if (_selMod < 0) return;
        _flags[_selMod].DefaultValue = null;
        txtDefaultVal.Text = "";
        RefreshMod(); UpdateFlagInfoLabel();
        SetStatus($"Default cleared: {_flags[_selMod].Name}");
    }
    void DoAdicionar()
    {
        using var dlg = new AddFlagDialog(_flags) { StatusCallback = SetStatus };
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result.Count == 0) return;
        foreach (var entry in dlg.Result)
            _flags.Add(entry);
        RegisterFlagHotkeys();
        RefreshMod(); RefreshAvail();
        if (dlg.Result.Count == 1)
        {
            var f = dlg.Result[0];
            SetStatus($"Added: {f.Name} = {f.Value}");
            _engine.WriteOne(f, _loader.Offsets, _scanner, _feat.OffsetlessEnabled, _feat.DiskFallbackEnabled);
        }
        else
        {
            SetStatus($"{dlg.Result.Count} flags added.{(_engine.IsAttached ? " Applying..." : "")}");
            if (_engine.IsAttached) TriggerApply();
        }
    }
    void DoExport()
    {
        if (_flags.Count == 0) { MessageBox.Show("No flags to export."); return; }
        using var dlg = new SaveFileDialog { Filter = "JSON Files|*.json", FileName = "flags_export.json" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, FlagParser.ToJson(_flags), Encoding.UTF8);
            SetStatus($"Exported {_flags.Count} flags.");
        }
        catch (Exception ex) { MessageBox.Show($"Export failed:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    void DoRemoveAll()
    {
        if (_flags.Count == 0) return;
        if (MessageBox.Show("Remove ALL flags?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _flags.Clear(); _selMod = -1;
        RegisterFlagHotkeys();
        ClearActions(); RefreshMod(); RefreshAvail();
        SetStatus("All flags removed.");
    }
    void DoUninject()
    {
        if (!_engine.IsAttached) { SetStatus("Roblox not attached"); return; }
        int restored = _engine.Restore();
        foreach (var f in _flags)
            f.Enabled = false;
        RegisterFlagHotkeys();
        RefreshMod();
        SetStatus($"Restored {restored}/{_flags.Count} flags.");
    }
    void TriggerApply() => _ = ApplyAllAsync();
    async Task ApplyWhenReadyAsync()
    {
        if (await Task.Run(() => _engine.IsGameReady()))
        {
            await ApplyAllAsync();
            return;
        }
        SetStatus("Waiting for Roblox to load...");
        for (int attempt = 0; attempt < 60; attempt++)   
        {
            await Task.Delay(500);
            if (!_engine.IsAttached) return;  
            if (await Task.Run(() => _engine.IsGameReady()))
            {
                SetStatus("Roblox ready — injecting...");
                await ApplyAllAsync();
                return;
            }
        }
        SetStatus("DataModel timeout — injecting anyway...");
        await ApplyAllAsync();
    }
    async Task ApplyAllAsync()
    {
        if (!_engine.IsAttached) { SetStatus("Roblox not attached"); return; }
        if (_featEng?.CheckEnv() ?? false) { SetStatus("[Stealth] Recording detected — skipped."); return; }
        SetStatus("Applying flags...");
        var snapshot     = new List<FlagEntry>(_flags);
        int enabledCount = snapshot.Count(f => f.Enabled);
        var (applied, noOffset, writeFail, diskFallback) = await Task.Run(
            () => _engine.WriteAll(snapshot, _loader.Offsets, _scanner, _feat));
        var parts = new List<string> { $"✔ {applied}/{enabledCount} applied" };
        if (noOffset     > 0) parts.Add($"{noOffset} no offset");
        if (writeFail    > 0) parts.Add($"{writeFail} write fail");
        if (diskFallback > 0) parts.Add($"{diskFallback} via disk");
        SetStatus(string.Join("  |  ", parts));
    }
    void SetStatus(string msg)
    {
        if (InvokeRequired) Invoke(() => tsslStatus.Text = msg);
        else tsslStatus.Text = msg;
    }
    void SetRobloxStatus(string msg, Color color)
    {
        if (InvokeRequired) Invoke(() => { tsslRoblox.Text = msg; tsslRoblox.ForeColor = color; });
        else { tsslRoblox.Text = msg; tsslRoblox.ForeColor = color; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int hotkeyId = m.WParam.ToInt32();
            if (hotkeyId == HK_APPLY) TriggerApply();
            else if (_flagHotkeys.TryGetValue(hotkeyId, out int index)) ToggleFlagAt(index);
        }
        base.WndProc(ref m);
    }
}
