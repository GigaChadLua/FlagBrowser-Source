using System.Text.Json;

namespace FlagInjector;

public class ProfileManager
{
    static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlagInjector", "profiles");

    public ProfileManager() => Directory.CreateDirectory(_dir);

    public IEnumerable<string> List() =>
        Directory.GetFiles(_dir, "*.json")
                 .Select(Path.GetFileNameWithoutExtension)
                 .Where(n => n is not null)
                 .Select(n => n!);

    public void Save(Profile p) =>
        File.WriteAllText(
            Path.Combine(_dir, Sanitize(p.Name) + ".json"),
            JsonSerializer.Serialize(p, _jsonOpts));

    public Profile? Load(string name)
    {
        var path = Path.Combine(_dir, Sanitize(name) + ".json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<Profile>(File.ReadAllText(path));
    }

    public void Delete(string name)
    {
        var path = Path.Combine(_dir, Sanitize(name) + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    static string Sanitize(string s) =>
        string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
}
