using System.Text.Json;
using System.Text.Json.Serialization;
namespace FlagInjector;
public sealed class AppSettings
{
    static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlagInjector", "settings.json");
    static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };
    public int WindowWidth  { get; set; } = 1050;
    public int WindowHeight { get; set; } = 720;
    public int SplitterPos  { get; set; } = 380;
    public bool ShowPresets { get; set; } = true;
    public string DefaultValuesPath { get; set; } = "";
    public bool DarkMode { get; set; } = false;
    public bool AutoCheckUpdates { get; set; } = true;
    public string UpdateManifestUrl { get; set; } = "https://github.com/GigaChadLua/flagbrowser/releases/latest/download/manifest.json";
    public static AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            var migrated = new AppSettings();
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
