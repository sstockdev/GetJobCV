using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

var options = ParseArgs(args);
string sourceDirectory = Require(options, "--source");
string outFile = options.GetValueOrDefault("--out", Path.Combine("Resources", "onet_skills.tsv"));
string version = options.GetValueOrDefault("--version", "30.3");
string blockPath = options.GetValueOrDefault("--blocklist", Path.Combine(AppContext.BaseDirectory, "blocklist.txt"));

int minLen = int.Parse(options.GetValueOrDefault("--min-length", "2"));
bool hotOnly = options.ContainsKey("--hot-only");
bool excludeTools = options.ContainsKey("--exclude-tools");

var sources = new List<Source>
{
    new("Software Skills.txt", "Workplace Example", "Technology", "Hot Technology", "In Demand"),
    new("Essential Skills.txt", "Element Name", "Competency", null, null),
    new("Transferable Skills.txt", "Element Name", "Competency", null, null)
};

if (excludeTools) sources.RemoveAll(s => s.Category == "Tool");

var block = LoadBlocklist(blockPath);
Console.WriteLine($"blocklist: {block.Count} terms (from {blockPath})");

var seen = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
var provenance = new List<string>();

foreach (var source in sources)
{
    string path = Path.Combine(sourceDirectory, source.File);
    if (!File.Exists(path)) { Console.WriteLine($"Warning: {source.File} is missing, skipping"); continue; }

    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    provenance.Add($"#   {source.File}  sha256={Sha256(path)}");
    if (lines.Length < 2) continue;

    string[] headers = lines[0].Split('\t');
    int iName = Array.IndexOf(headers, source.NameCol);
    int iHot = source.HotCol is null ? -1 : Array.IndexOf(headers, source.HotCol);
    int iDemand = source.DemandCol is null ? -1 : Array.IndexOf(headers, source.DemandCol);
    if (iName < 0)
        throw new InvalidOperationException($"column '{source.NameCol}' is not in {source.File}.\nHeaders:\n{string.Join(", ", headers)}");

    int added = 0, blocked = 0;
    for (int i = 1; i < lines.Length; i++)
    {
        string[] f = lines[i].Split('\t');
        if (iName >= f.Length) continue;

        string name = Regex.Replace(f[iName], @"\s+", " ").Trim();
        if (name.Length < minLen) continue;
        if (block.Contains(name)) { blocked++; continue; }

        int weight = (iHot >= 0 && iHot < f.Length && f[iHot].Trim() == "Y") ? 2 : 
            (iDemand >= 0 && iDemand < f.Length && f[iDemand].Trim() == "Y") ? 1 : 0;

        if (hotOnly && weight < 2) continue;

        if (seen.TryGetValue(name, out var previous))
        {
            if (weight > previous.Weight) seen[name] = previous with { Weight = weight };
            continue;
        }

        seen[name] = new Entry(name, source.Category, weight);
        added++;
    }

    Console.WriteLine($"{source.File,-22} {lines.Length - 1,7} rows -> {added,6} new, {blocked} blocked");
}
    var entries = seen.Values.OrderBy(e => e.Category).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    var header = new List<string>
    {
        "# O*NET skill gazetteer",
        $"# Source: O*NET {version} Database",
        "# O*NET data (c) US DOL/ETA, used under CC BY 4.0. https://www.onetcenter.org/",
        "# Provenance (SHA256):"
    };

    header.AddRange(provenance);
    header.Add("# Format: <skill><TAB><category><TAB><weight>  (weight: 2=hot, 1=in-demand, 0=other)");
    header.Add($"# Total: {entries.Count} skills");

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);

    using var sw = new StreamWriter(outFile, false, new UTF8Encoding(false));
    foreach (var l in header) sw.WriteLine(l);
    foreach (var e in entries)
        sw.WriteLine($"{e.Name}\t{e.Category}\t{e.Weight}");

    Console.WriteLine($"wrote {entries.Count} skills -> {outFile}");

    static Dictionary<string, string> ParseArgs(string[] a)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].StartsWith("--"))
                d[a[i]] = (i + 1 < a.Length && !a[i + 1].StartsWith("--")) ? a[++i] : "true";
        }

        return d;
    }

    static string Require(Dictionary<string, string> d, string k) =>
        d.TryGetValue(k, out var v) && v != "true" ? v : throw new ArgumentException($"missing required arg {k}");

    static HashSet<string> LoadBlocklist(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return set;

        foreach (var l in File.ReadAllLines(path, Encoding.UTF8))
        {
            var t = l.Trim();
            if (t.Length > 0 && !t.StartsWith('#')) set.Add(t);
        }

        return set;
    }

    static string Sha256(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    record Source(string File, string NameCol, string Category, string? HotCol, string? DemandCol);

    record Entry(string Name, string Category, int Weight);