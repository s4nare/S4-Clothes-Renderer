using System.Text.RegularExpressions;

namespace S4ClothesRenderer;

public sealed record ColorVariant(char Letter, FileRef Texture);

public sealed class ClothingItem
{
    public string Sex { get; init; }
    public string Comp { get; init; }
    public string Collection { get; init; }
    public string Model { get; init; }
    public string PropSuffix { get; init; }
    public int Num { get; init; }
    public string Name { get; init; }
    public FileRef Drawable { get; init; }
    public List<ColorVariant> Colors { get; init; }
}

public static class Catalog
{
    private static readonly Regex FileName = new(@"^(mp_[mf]_freemode_01)(_p)?(?:_(.+))?\^(p_[a-z]+|[a-z]+)_(\d{3})(?:_([a-z]))?(?:_\d+)?\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TextureName = new(@"^(.*\^[a-z_]+_diff_\d{3})_([a-z])(?:_[a-z]+)?\.ytd$", RegexOptions.Compiled);
    private static readonly Regex FreemodeFolder = new(@"^mp_[mf]_freemode_01(_p)?(_.+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Universal = new(@"_u(_\d+)?\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Dictionary<string, string> SexOf = new() { ["mp_m_freemode_01"] = "male", ["mp_f_freemode_01"] = "female" };

    public static List<(string FullName, FileRef File)> ReadFolder(string root)
    {
        var files = new List<(string, FileRef)>();
        if (!Directory.Exists(root)) return files;
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".ydd" && ext != ".ytd") continue;
            files.Add((FullName(file), new FileRef(file)));
        }
        return files;
    }

    private static string FullName(string file)
    {
        var name = Path.GetFileName(file);
        if (name.Contains('^')) return name;
        var folder = Path.GetFileName(Path.GetDirectoryName(file));
        return FreemodeFolder.IsMatch(folder ?? "") ? folder + "^" + name : name;
    }

    public static List<ClothingItem> Build(IEnumerable<List<(string FullName, FileRef File)>> roots)
    {
        var items = new Dictionary<string, ClothingItem>();
        var all = new Dictionary<string, SortedDictionary<char, FileRef>>();
        var perRoot = new List<(List<(string FullName, FileRef File)> Files, Dictionary<string, SortedDictionary<char, FileRef>> Textures)>();
        foreach (var files in roots)
        {
            var textures = new Dictionary<string, SortedDictionary<char, FileRef>>();
            foreach (var (fullName, file) in files)
            {
                var m = TextureName.Match(fullName.ToLowerInvariant());
                if (!m.Success) continue;
                if (!textures.TryGetValue(m.Groups[1].Value, out var colors)) textures[m.Groups[1].Value] = colors = [];
                colors.TryAdd(m.Groups[2].Value[0], file);
                if (!all.TryGetValue(m.Groups[1].Value, out var shared)) all[m.Groups[1].Value] = shared = [];
                shared[m.Groups[2].Value[0]] = file;
            }
            perRoot.Add((files, textures));
        }
        foreach (var (files, textures) in perRoot)
        {
            var models = files.Where(f => f.FullName.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Universal.IsMatch(f.FullName) ? 0 : 1).ToList();
            var seen = new HashSet<string>();
            foreach (var (fullName, file) in models)
            {
                var m = FileName.Match(fullName);
                if (!m.Success) continue;
                var comp = m.Groups[4].Value.ToLowerInvariant();
                if (!Component.ByKey.ContainsKey(comp)) continue;
                var model = m.Groups[1].Value.ToLowerInvariant();
                var propSuffix = m.Groups[2].Value.ToLowerInvariant();
                var collection = m.Groups[3].Value.ToLowerInvariant();
                var key = $"{SexOf[model]}/{comp}/{collection}/{m.Groups[5].Value}";
                if (!seen.Add(key)) continue;
                var texKey = (m.Groups[1].Value + m.Groups[2].Value + (m.Groups[3].Success ? "_" + m.Groups[3].Value : "") + "^" + m.Groups[4].Value + "_diff_" + m.Groups[5].Value).ToLowerInvariant();
                var colors = textures.TryGetValue(texKey, out var found) || all.TryGetValue(texKey, out found) ? found.Select(c => new ColorVariant(c.Key, c.Value)).ToList() : [];
                items[key] = new ClothingItem
                {
                    Sex = SexOf[model], Comp = comp, Collection = collection, Model = model, PropSuffix = propSuffix,
                    Num = int.Parse(m.Groups[5].Value), Name = comp + "_" + m.Groups[5].Value, Drawable = file, Colors = colors,
                };
            }
        }
        return [.. items.Values];
    }
}
