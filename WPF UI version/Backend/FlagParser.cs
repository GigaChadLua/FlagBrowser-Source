using System.Text;
using System.Text.RegularExpressions;

namespace FlagInjector;











public static class FlagParser
{
    
    static readonly Regex _rx = new(
        "\\\"([^\\\"]+)\\\"\\s*:\\s*(?:\\\"((?:[^\\\"\\\\]|\\\\.)*)\\\"|([A-Za-z0-9._+\\-]+))",
        RegexOptions.Compiled);

    
    
    
    
    public static List<FlagEntry> Parse(string json)
    {
        var list = new List<FlagEntry>();
        foreach (Match m in _rx.Matches(json))
        {
            string name = m.Groups[1].Value;
            string val  = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(val))
                list.Add(new FlagEntry(name, val));
        }
        return list;
    }

    
    
    
    
    
    public static (int added, int skipped) MergeInto(
        List<FlagEntry>        target,
        IEnumerable<FlagEntry> source)
    {
        var existing = target
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0, skipped = 0;
        foreach (var f in source)
        {
            if (existing.Contains(f.Name)) { skipped++; continue; }
            f.DefaultValue ??= FlagDefaults.Instance.Get(f.Name);
            target.Add(f);
            existing.Add(f.Name);
            added++;
        }
        return (added, skipped);
    }

    
    
    
    
    public static string ToJson(IReadOnlyList<FlagEntry> flags)
    {
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < flags.Count; i++)
        {
            var    f   = flags[i];
            string v   = FormatValue(f.Value, f.Type);
            string sep = i < flags.Count - 1 ? "," : "";
            sb.AppendLine($"  \"{f.Name}\": {v}{sep}");
        }
        sb.Append('}');
        return sb.ToString();
    }

    static string FormatValue(string val, string type) => type switch
    {
        "bool"  => val.ToLowerInvariant() is "true" or "1" ? "true" : "false",
        "int"   => int.TryParse(val, out int n)
                   ? n.ToString()
                   : $"\"{val}\"",
        "float" => double.TryParse(val, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out double d)
                   ? d.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   : $"\"{val}\"",
        _       => $"\"{val}\"",
    };
}
