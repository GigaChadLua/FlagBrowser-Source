using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlagInjector;

public sealed class AppSettings
{
    static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlagInjector", "settings.json");

    static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public string Url1          { get; set; } = "";
    public string Url2          { get; set; } = "";
    public string LocalFilePath { get; set; } = "";
    public bool   UseLocalFile  { get; set; } = false;

    public int WindowWidth  { get; set; } = 1050;
    public int WindowHeight { get; set; } = 720;
    public int SplitterPos  { get; set; } = 380;

    public string LastProfile { get; set; } = "";

    public bool ShowPresets { get; set; } = true;

    public string DefaultValuesPath { get; set; } = "";

    public bool AutoCheckUpdates { get; set; } = true;
    public string UpdateManifestUrl { get; set; } = "";

    public static AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            var migrated = new AppSettings();
            string dir = Path.GetDirectoryName(_path)!;
            string R(string f) { var p = Path.Combine(dir, f); return File.Exists(p) ? File.ReadAllText(p).Trim() : ""; }
            string u1 = R("url1.txt");     if (!string.IsNullOrEmpty(u1)) migrated.Url1          = u1;
            string u2 = R("url2.txt");     if (!string.IsNullOrEmpty(u2)) migrated.Url2          = u2;
            string fp = R("filepath.txt"); if (!string.IsNullOrEmpty(fp)) migrated.LocalFilePath = fp;
            string ul = R("uselocal.txt"); migrated.UseLocalFile = ul == "1";
            migrated.Save();
            return migrated;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), _opts) ?? new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, _opts));
        }
        catch { }
    }
}
