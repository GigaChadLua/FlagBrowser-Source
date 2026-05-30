using System.Text;
namespace FlagInjector;
public class AddFlagDialog : Form
{
    public List<FlagEntry> Result { get; } = new();
    TabControl  _tabs      = null!;
    TabPage     _tabSingle = null!;
    TabPage     _tabJson   = null!;
    TextBox     _txtName   = null!;
    TextBox     _txtValue  = null!;
    RichTextBox _rtJson    = null!;
    readonly IReadOnlyList<FlagEntry> _existing;
    public AddFlagDialog(IReadOnlyList<FlagEntry> existingFlags)
    {
        _existing       = existingFlags;
        Text            = "Add Fast Flag";
        Size            = new Size(460, 340);
        MinimumSize     = new Size(400, 300);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        Font            = new Font("Segoe UI", 9f);
        BackColor       = Color.FromArgb(28, 28, 36);
        ForeColor       = Color.White;
        Build();
    }
    void Build()
    {
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10f),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(120, 28)
        };
        _tabs.DrawItem += DrawTab;
        _tabSingle = new TabPage("Single Flag") { BackColor = Color.FromArgb(28, 28, 36), ForeColor = Color.White };
        _tabJson   = new TabPage("Importar JSON")   { BackColor = Color.FromArgb(28, 28, 36), ForeColor = Color.White };
        _tabs.TabPages.Add(_tabSingle);
        _tabs.TabPages.Add(_tabJson);
        BuildSingleTab();
        BuildJsonTab();
        var pnlBtn  = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(22, 22, 30) };
        var btnOk   = MakeBtn("Ok",        Color.FromArgb(50, 80, 180)); btnOk.Anchor   = AnchorStyles.Right | AnchorStyles.Top;
        var btnCxl  = MakeBtn("Cancel",  Color.FromArgb(55, 55, 65));  btnCxl.Anchor  = AnchorStyles.Right | AnchorStyles.Top;
        btnOk.DialogResult  = DialogResult.None;  
        btnCxl.DialogResult = DialogResult.Cancel;
        btnOk.Click += OnOk;
        pnlBtn.Resize += (_, _) =>
        {
            btnCxl.Left = pnlBtn.Width - 98;  btnCxl.Top = 9;
            btnOk.Left  = btnCxl.Left - 98;   btnOk.Top  = 9;
        };
        pnlBtn.Controls.AddRange(new Control[] { btnOk, btnCxl });
        Controls.Add(_tabs);
        Controls.Add(pnlBtn);
        AcceptButton = btnOk;
        CancelButton = btnCxl;
    }
    void DrawTab(object? sender, DrawItemEventArgs e)
    {
        var page = _tabs.TabPages[e.Index];
        bool selected = e.Index == _tabs.SelectedIndex;
        Color bgColor = selected ? Color.FromArgb(28, 28, 28) : Color.FromArgb(18, 18, 18);
        Color fgColor = selected ? Color.White : Color.Silver;
        using var bg = new SolidBrush(bgColor);
        using var border = new Pen(Color.FromArgb(70, 70, 70));
        e.Graphics.FillRectangle(bg, e.Bounds);
        e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        TextRenderer.DrawText(
            e.Graphics,
            page.Text,
            _tabs.Font,
            e.Bounds,
            fgColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    static Button MakeBtn(string text, Color bg)
    {
        var b = new Button { Text = text, Width = 90, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White };
        b.UseVisualStyleBackColor = false;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 48, 48);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(28, 28, 28);
        return b;
    }
    void BuildSingleTab()
    {
        const int lx = 12;
        int y = 16;
        var lblName = new Label { Text = "Flag name:", Left = lx, Top = y, AutoSize = true, ForeColor = Color.Silver };
        _txtName = MakeTextBox(lx, y + 20, "e.g. FFlagDebugDisplayFPS");
        y += 60;
        var lblVal = new Label { Text = "Value:", Left = lx, Top = y, AutoSize = true, ForeColor = Color.Silver };
        _txtValue = MakeTextBox(lx, y + 20, "true, false, 100, ...");
        _tabSingle.Resize += (_, _) =>
        {
            _txtName.Width  = _tabSingle.Width - lx * 2 - 12;
            _txtValue.Width = _tabSingle.Width - lx * 2 - 12;
        };
        _tabSingle.Controls.AddRange(new Control[] { lblName, _txtName, lblVal, _txtValue });
    }
    TextBox MakeTextBox(int left, int top, string placeholder) => new()
    {
        Left            = left, Top = top,
        Width           = 420,
        Anchor          = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        PlaceholderText = placeholder,
        BackColor       = Color.FromArgb(38, 40, 54),
        ForeColor       = Color.White,
        BorderStyle     = BorderStyle.FixedSingle,
    };
    void BuildJsonTab()
    {
        _rtJson = new RichTextBox
        {
            Left        = 10, Top = 10,
            Width       = _tabJson.Width - 20,
            Height      = _tabJson.Height - 62,
            Anchor      = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
            BackColor   = Color.FromArgb(38, 40, 54),
            ForeColor   = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font        = new Font("Consolas", 9f),
            Text        = "{\n  \n}",
            ScrollBars  = RichTextBoxScrollBars.Vertical,
        };
        var btnFile = new Button
        {
            Text      = "Import from file",
            Left      = 10, Top = _tabJson.Height - 44,
            Width     = _tabJson.Width - 20, Height = 30,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 40, 54),
            ForeColor = Color.White,
        };
        btnFile.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 100);
        btnFile.Click += (_, _) => BrowseJsonFile();
        _tabJson.Controls.Add(_rtJson);
        _tabJson.Controls.Add(btnFile);
    }
    void BrowseJsonFile()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON Files|*.json|All Files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try   { _rtJson.Text = File.ReadAllText(dlg.FileName, Encoding.UTF8); }
        catch (Exception ex) { MessageBox.Show("Erro ao ler arquivo:\n" + ex.Message); }
    }
    void OnOk(object? s, EventArgs e)
    {
        Result.Clear();
        if (_tabs.SelectedTab == _tabSingle)
        {
            string name = _txtName.Text.Trim();
            string val  = _txtValue.Text.Trim();
            if (name.Length == 0) { MessageBox.Show("Enter the flag name.", "Aviso"); return; }
            if (val.Length  == 0) { MessageBox.Show("Enter the flag value.", "Aviso"); return; }
            if (IsDuplicate(name))
            {
                MessageBox.Show($"\"{name}\" is already in the list.", "Duplicate");
                DialogResult = DialogResult.OK; Close(); return;
            }
            Result.Add(new FlagEntry(name, val) { DefaultValue = FlagDefaults.Instance.Get(name) });
        }
        else
        {
            string json = _rtJson.Text.Trim();
            if (json.Length == 0 || json == "{\n  \n}") { MessageBox.Show("Paste or import a JSON first.", "Aviso"); return; }
            List<FlagEntry> parsed;
            try   { parsed = FlagParser.Parse(json); }
            catch (Exception ex) { MessageBox.Show("JSON inválido:\n" + ex.Message); return; }
            if (parsed.Count == 0) { MessageBox.Show("No flags found in JSON.", "Import"); return; }
            int skipped = 0;
            foreach (var f in parsed)
            {
                if (IsDuplicate(f.Name)) { skipped++; continue; }
                f.DefaultValue ??= FlagDefaults.Instance.Get(f.Name);
                Result.Add(f);
            }
            if (skipped > 0)
                StatusCallback?.Invoke($"⚠ {skipped} flag(s) skipped (already in list).");
        }
        DialogResult = DialogResult.OK;
        Close();
    }
    bool IsDuplicate(string name) =>
        _existing.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public Action<string>? StatusCallback { get; set; }
}
