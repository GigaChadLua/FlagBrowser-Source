namespace FlagInjector;
public class SettingsForm : Form
{
    readonly FeatureSettings     _cfg;
    readonly AppSettings         _appCfg;
    readonly Action<FeatureSettings> _onSave;
    CheckBox      chkDarkMode    = new();
    CheckBox      chkSafeMode    = new();
    CheckBox      chkReApply     = new();
    NumericUpDown nudReApply     = new();
    CheckBox      chkRandom      = new();
    NumericUpDown nudRandMin     = new();
    NumericUpDown nudRandMax     = new();
    CheckBox      chkTiming      = new();
    NumericUpDown nudTiming      = new();
    CheckBox      chkOffsetless  = new();
    CheckBox      chkStealth     = new();
    CheckBox      chkDiskFallback = new();
    CheckBox      chkShuffle      = new();
    NumericUpDown nudBatch       = new();
    NumericUpDown nudDelay       = new();
    Label lblElevation = new();
    public SettingsForm(FeatureSettings cfg, AppSettings appCfg, Action<FeatureSettings> onSave)
    {
        _cfg    = cfg;
        _appCfg = appCfg;
        _onSave = onSave;
        Text            = "Settings";
        Size            = new Size(490, 680);
        MinimumSize     = new Size(450, 600);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        Font            = new Font("Segoe UI", 9f);
        InitControls();
        BuildLayout();
        LoadValues();
    }
    void InitControls()
    {
        void SetNud(NumericUpDown n, decimal min, decimal max, decimal val)
        {
            n.Minimum = min; n.Maximum = max; n.Value = val;
            n.Width = 90; n.Height = 24;
        }
        chkSafeMode    = new CheckBox { Text = "Enable Safe Mode", AutoSize = true };
        chkReApply     = new CheckBox { Text = "Re-apply flags at fixed interval",              AutoSize = true };
        nudReApply     = new NumericUpDown(); SetNud(nudReApply, 500, 60000, 5000);
        chkRandom      = new CheckBox { Text = "Re-apply at random intervals",  AutoSize = true };
        nudRandMin     = new NumericUpDown(); SetNud(nudRandMin, 1000, 30000, 3000);
        nudRandMax     = new NumericUpDown(); SetNud(nudRandMax, 1000, 60000, 10000);
        chkTiming      = new CheckBox { Text = "[BETA] Apply flags early on Roblox attach", AutoSize = true };
        nudTiming      = new NumericUpDown(); SetNud(nudTiming, 0, 5000, 300);
        chkOffsetless  = new CheckBox { Text = "Enable alternate lookup", AutoSize = true };
        chkStealth     = new CheckBox { Text = "Pause applies while recording", AutoSize = true };
        chkDiskFallback = new CheckBox { Text = "Enable disk backup", AutoSize = true };
        chkShuffle      = new CheckBox { Text = "Shuffle apply order", AutoSize = true };
        chkDarkMode     = new CheckBox { Text = "Dark mode", AutoSize = true };
        nudBatch       = new NumericUpDown(); SetNud(nudBatch, 1, 200, 20);
        nudDelay       = new NumericUpDown(); SetNud(nudDelay, 0, 500, 15);
        bool selfElevated = FeatureEngine.IsSelfElevated();
        lblElevation = new Label
        {
            AutoSize  = true,
            ForeColor = selfElevated ? Color.DarkGreen : Color.DarkOrange,
            Text      = selfElevated
                ? "✔ Running as Administrator — writes should work."
                : "⚠ NOT running as Administrator — writes may fail if Roblox is elevated.",
        };
    }
    void BuildLayout()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 10;
        int lx = 14;
        Section(scroll, "🔑  Admin Status", ref y, lx);
        Row(scroll, lblElevation, ref y, lx);
        Section(scroll, "🛡  Safe Mode", ref y, lx);
        Row(scroll, chkSafeMode, ref y, lx);
        Desc(scroll, "Uses the compatibility write path.", ref y, lx);
        Section(scroll, "🔁  Re-apply", ref y, lx);
        Row(scroll, chkReApply, ref y, lx);
        NudRow(scroll, "Interval (ms):", nudReApply, ref y, lx);
        Desc(scroll, "Re-applies all flags periodically to keep them active.", ref y, lx);
        Section(scroll, "🎲  Random Re-apply", ref y, lx);
        Row(scroll, chkRandom, ref y, lx);
        NudRow(scroll, "Min (ms):", nudRandMin, ref y, lx);
        NudRow(scroll, "Max (ms):", nudRandMax, ref y, lx);
        Desc(scroll, "Varies the interval between apply cycles.", ref y, lx);
        Section(scroll, "⚡  Early Apply  [BETA]", ref y, lx, Color.DarkOrange);
        Row(scroll, chkTiming, ref y, lx);
        NudRow(scroll, "Delay after attach (ms):", nudTiming, ref y, lx);
        Desc(scroll, "Runs the apply step earlier during startup.", ref y, lx, Color.DarkOrange);
        Section(scroll, "🔍  Alternate Lookup", ref y, lx, Color.DarkSlateBlue);
        Row(scroll, chkOffsetless, ref y, lx);
        Desc(scroll, "Uses an alternate flag lookup path. Enabled by default.", ref y, lx, Color.DarkSlateBlue);
        Section(scroll, "💾  Disk Backup", ref y, lx, Color.DarkCyan);
        Row(scroll, chkDiskFallback, ref y, lx);
        Desc(scroll, "Stores pending values on disk when direct apply is unavailable.", ref y, lx, Color.DarkCyan);
        Section(scroll, "🔀  Apply Order", ref y, lx);
        Row(scroll, chkShuffle, ref y, lx);
        Desc(scroll, "Changes the order used for each apply cycle.", ref y, lx);
        Section(scroll, "👁  Recording Guard", ref y, lx, Color.Gray);
        Row(scroll, chkStealth, ref y, lx);
        Desc(scroll, "Pauses apply cycles while common recording tools are active.", ref y, lx, Color.Gray);
        Section(scroll, "UI", ref y, lx);
        Row(scroll, chkDarkMode, ref y, lx);
        Desc(scroll, "Applies a dark palette without adding controls to the main toolbar.", ref y, lx);
        Section(scroll, "⚙  Batch Settings", ref y, lx);
        NudRow(scroll, "Batch size (flags/group):", nudBatch, ref y, lx);
        NudRow(scroll, "Delay between batches (ms):", nudDelay, ref y, lx);
        Desc(scroll, "Recommended: batch 20, delay 15ms.", ref y, lx);
        var pnlBtn = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var btnSave   = new Button { Text = "Save",   Width = 90, Height = 30, Left = 280, Top = 8 };
        var btnCancel = new Button { Text = "Cancel", Width = 90, Height = 30, Left = 374, Top = 8 };
        btnSave.BackColor = Color.FromArgb(34, 110, 34);
        btnSave.ForeColor = Color.White;
        btnSave.FlatStyle = FlatStyle.Flat;
        btnSave.Click   += (_, _) => { SaveValues(); _onSave(_cfg); Close(); };
        btnCancel.Click += (_, _) => Close();
        pnlBtn.Controls.AddRange(new Control[] { btnSave, btnCancel });
        Controls.Add(scroll);
        Controls.Add(pnlBtn);
    }
    static void Section(Panel p, string text, ref int y, int lx, Color? color = null)
    {
        y += 6;
        p.Controls.Add(new Label
        {
            Left = lx, Top = y, AutoSize = true, Text = text,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = color ?? Color.FromArgb(30, 30, 30),
        });
        y += 22;
        p.Controls.Add(new Panel { Left = lx, Top = y, Width = 440, Height = 1, BackColor = Color.LightGray });
        y += 5;
    }
    static void Row(Panel p, Control ctrl, ref int y, int lx)
    {
        ctrl.Left = lx + 4; ctrl.Top = y;
        p.Controls.Add(ctrl);
        y += ctrl.Height + 4;
    }
    static void NudRow(Panel p, string label, NumericUpDown nud, ref int y, int lx)
    {
        p.Controls.Add(new Label { Left = lx + 14, Top = y + 3, AutoSize = true, Text = label });
        nud.Left = 230; nud.Top = y;
        p.Controls.Add(nud);
        y += 28;
    }
    static void Desc(Panel p, string text, ref int y, int lx, Color? color = null)
    {
        var lbl = new Label
        {
            Left = lx + 14, Top = y, Width = 420,
            Text = text, AutoSize = false,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = color ?? Color.Gray,
        };
        int lines = text.Split('\n').Length;
        lbl.Height = lines * 14 + 4;
        p.Controls.Add(lbl);
        y += lbl.Height + 4;
    }
    void Clamp(NumericUpDown n, decimal v) =>
        n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
    void LoadValues()
    {
        chkSafeMode.Checked     = _cfg.SafeMode;
        chkReApply.Checked      = _cfg.ReApplyEnabled;
        Clamp(nudReApply,         _cfg.ReApplyIntervalMs);
        chkRandom.Checked       = _cfg.RandomReApply;
        Clamp(nudRandMin,         _cfg.RandomMinMs);
        Clamp(nudRandMax,         _cfg.RandomMaxMs);
        chkTiming.Checked       = _cfg.TimingAttack;
        Clamp(nudTiming,          _cfg.TimingDelayMs);
        chkOffsetless.Checked   = _cfg.OffsetlessEnabled;
        chkStealth.Checked      = _cfg.StealthMode;
        chkDiskFallback.Checked = _cfg.DiskFallbackEnabled;
        chkShuffle.Checked      = _cfg.ShuffleEnabled;
        chkDarkMode.Checked     = _appCfg.DarkMode;
        Clamp(nudBatch,           _cfg.BatchSize);
        Clamp(nudDelay,           _cfg.BatchDelayMs);
    }
    void SaveValues()
    {
        _cfg.SafeMode           = chkSafeMode.Checked;
        _cfg.ReApplyEnabled     = chkReApply.Checked;
        _cfg.ReApplyIntervalMs  = (int)nudReApply.Value;
        _cfg.RandomReApply      = chkRandom.Checked;
        _cfg.RandomMinMs        = (int)nudRandMin.Value;
        _cfg.RandomMaxMs        = (int)nudRandMax.Value;
        _cfg.TimingAttack       = chkTiming.Checked;
        _cfg.TimingDelayMs      = (int)nudTiming.Value;
        _cfg.OffsetlessEnabled  = chkOffsetless.Checked;
        _cfg.StealthMode        = chkStealth.Checked;
        _cfg.DiskFallbackEnabled = chkDiskFallback.Checked;
        _cfg.ShuffleEnabled     = chkShuffle.Checked;
        _appCfg.DarkMode         = chkDarkMode.Checked;
        _appCfg.Save();
        _cfg.BatchSize          = (int)nudBatch.Value;
        _cfg.BatchDelayMs       = (int)nudDelay.Value;
        _cfg.Save();
    }
}
