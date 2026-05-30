using System.Text.RegularExpressions;

namespace FlagInjector;











public sealed class FlagDefaults
{
    
    static FlagDefaults? _instance;
    public static FlagDefaults Instance => _instance ??= new();

    static readonly Regex _rx = new(
        @"inline\s+constexpr\s+auto\s+(\w+)\s*=\s*([^;]+);",
        RegexOptions.Compiled);

    readonly Dictionary<string, string> _defaults =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _defaults.Count;
    public bool IsLoaded => _defaults.Count > 0;

    

    public (int count, string? error) LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return (0, "File not found");
            var body = File.ReadAllText(path);
            return Parse(body);
        }
        catch (Exception ex) { return (0, ex.Message); }
    }

    public (int count, string? error) LoadText(string body)
    {
        try   { return Parse(body); }
        catch (Exception ex) { return (0, ex.Message); }
    }

    public (int count, string? error) LoadEmbedded()
    {
        try
        {
            var asm = typeof(FlagDefaults).Assembly;
            string? name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(".Resources.values.hpp", StringComparison.OrdinalIgnoreCase)
                                  || n.EndsWith(".values.hpp", StringComparison.OrdinalIgnoreCase));
            if (name is null) return (0, "Embedded defaults not found");

            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return (0, "Embedded defaults not found");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) { return (0, ex.Message); }
    }

    (int count, string? error) Parse(string body)
    {
        _defaults.Clear();
        foreach (Match m in _rx.Matches(body))
        {
            string name = m.Groups[1].Value.Trim();
            string raw  = m.Groups[2].Value.Trim();
            if (name.Length == 0) continue;

            
            string val = raw switch
            {
                "true"  => "true",
                "false" => "false",
                _       => raw.Trim('"', '\'')
            };

            _defaults[name] = val;
        }
        return _defaults.Count > 0
            ? (_defaults.Count, null)
            : (0, "No entries found â€” check file format");
    }


    public string? Get(string flagName)
    {
        if (_defaults.TryGetValue(flagName, out var v)) return v;

        string bare = InjectionEngine.StripPrefix(flagName);
        if (bare != flagName && _defaults.TryGetValue(bare, out v)) return v;

        return null;
    }

    public IEnumerable<string> AllNames => _defaults.Keys;


    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlagInjector", "values.hpp");
}
