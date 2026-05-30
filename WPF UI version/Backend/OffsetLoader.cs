using System.Net.Http;
using System.Text.RegularExpressions;

namespace FlagInjector;











public sealed class OffsetLoader
{
    

    static readonly Regex _rCs = new(
        @"public\s+const\s+long\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+);",
        RegexOptions.Compiled);

    static readonly Regex _rHppNs = new(
        @"namespace\s+\w+\s*\{([^}]+)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    static readonly Regex _rHppField = new(
        @"(?:inline\s+constexpr\s+)?uintptr_t\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+);",
        RegexOptions.Compiled);

    static readonly Regex _rDump = new(
        @"FFlags::([A-Za-z_][A-Za-z0-9_]{3,})\s*=\s*(0x[0-9A-Fa-f]+)",
        RegexOptions.Compiled);

    static readonly Regex _rLooseOffset = new(
        @"^\s*(?:(?:inline\s+constexpr\s+)?uintptr_t\s+|const\s+long\s+|public\s+const\s+long\s+)?([A-Za-z_][A-Za-z0-9_]{3,})\s*(?:=|:)\s*[""']?(0x[0-9A-Fa-f]+)[""']?\s*;?",
        RegexOptions.Compiled | RegexOptions.Multiline);

    
    static readonly Regex _rPtr   = new(@"Pointer\s*=\s*(0x[0-9A-Fa-f]+)", RegexOptions.Compiled);
    static readonly Regex _rToFlg = new(@"ToFlag\s*=\s*(0x[0-9A-Fa-f]+)",   RegexOptions.Compiled);
    static readonly Regex _rToVal = new(@"ToValue\s*=\s*(0x[0-9A-Fa-f]+)",  RegexOptions.Compiled);

    static readonly Regex _rValidName = new(
        @"^[A-Za-z_][A-Za-z0-9_]+$",
        RegexOptions.Compiled);

    static readonly HttpClient _http = new();

    

    public Dictionary<string, nint> Offsets { get; } = new(StringComparer.OrdinalIgnoreCase);

    
    string[]? _sortedCache;
    public string[] SortedNames => _sortedCache ??= Offsets.Keys.OrderBy(k => k).ToArray();

    
    public nint FlogDataBank { get; private set; }
    public nint StructToFlag  { get; private set; } = 0x30;  
    public nint StructToValue { get; private set; } = 0xC0;  

    

    public (int count, string? error) LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return (0, "File not found");
            var body = File.ReadAllText(path);
            return Load(body);
        }
        catch (Exception ex) { return (0, ex.Message); }
    }

    

    public async Task<(int count, List<string> errors)> LoadUrlsAsync(IEnumerable<string> urls)
    {
        
        
        Offsets.Clear();
        _sortedCache = null;
        FlogDataBank  = 0;

        var tasks = urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(async url =>
            {
                try   { return (url, body: await _http.GetStringAsync(url), err: (string?)null); }
                catch (Exception ex) { return (url, body: "", err: ex.Message); }
            });

        var results = await Task.WhenAll(tasks);
        var errors  = new List<string>();

        foreach (var (_, body, err) in results)
        {
            if (err is not null) { errors.Add(err); continue; }
            ParseInto(body, Offsets);
            TryLoadStructural(body);  
        }
        _sortedCache = null;
        return (Offsets.Count, errors);
    }

    

    public async Task<(string body, string? error)> DownloadAsync(string url)
    {
        try   { return (await _http.GetStringAsync(url), null); }
        catch (Exception ex) { return ("", ex.Message); }
    }

    

    (int count, string? error) Load(string body)
    {
        Offsets.Clear();
        _sortedCache = null;
        ParseInto(body, Offsets);
        TryLoadStructural(body);
        return Offsets.Count > 0 ? (Offsets.Count, null) : (0, "No valid offsets found");
    }

    static void ParseInto(string body, Dictionary<string, nint> offsets)
    {
        if (body.Contains("public const long"))
        {
            foreach (Match m in _rCs.Matches(body))
                TryAdd(m.Groups[1].Value, m.Groups[2].Value, offsets);
            return;
        }

        var ns = _rHppNs.Match(body);
        if (ns.Success)
        {
            foreach (Match m in _rHppField.Matches(ns.Groups[1].Value))
                TryAdd(m.Groups[1].Value, m.Groups[2].Value, offsets);
            return;
        }

        
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            ParseJson(body, offsets);
            return;
        }

        foreach (Match m in _rDump.Matches(body))
            TryAdd(m.Groups[1].Value, m.Groups[2].Value, offsets);

        foreach (Match m in _rLooseOffset.Matches(body))
            TryAdd(m.Groups[1].Value, m.Groups[2].Value, offsets);
    }

    static readonly Regex _rJsonEntry = new(
        "\\\"([A-Za-z_][A-Za-z0-9_]+)\\\"\\s*:\\s*\\\"?(0x[0-9A-Fa-f]+)\\\"?",
        RegexOptions.Compiled);

    static void ParseJson(string body, Dictionary<string, nint> offsets)
    {
        foreach (Match m in _rJsonEntry.Matches(body))
            TryAdd(m.Groups[1].Value, m.Groups[2].Value, offsets);
    }

    static void TryAdd(string name, string hexVal, Dictionary<string, nint> offsets)
    {
        if (name.Length < 4) return;
        if (!_rValidName.IsMatch(name)) return;
        if (!TryParseHex(hexVal, out nint addr) || addr == 0) return;
        offsets[name] = addr;
    }

    void TryLoadStructural(string body)
    {
        var mPtr = _rPtr.Match(body);
        var mFlg = _rToFlg.Match(body);
        var mVal = _rToVal.Match(body);

        if (mPtr.Success && TryParseHex(mPtr.Groups[1].Value, out var v))
            FlogDataBank = v;
        if (mFlg.Success && TryParseHex(mFlg.Groups[1].Value, out v))
            StructToFlag = v;
        if (mVal.Success && TryParseHex(mVal.Groups[1].Value, out v))
            StructToValue = v;
    }

    static bool TryParseHex(string s, out nint result)
    {
        s = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        if (long.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out long v))
        {
            result = (nint)v;
            return true;
        }
        result = 0;
        return false;
    }
}
