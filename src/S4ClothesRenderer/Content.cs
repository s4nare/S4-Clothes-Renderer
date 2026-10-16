using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using CodeWalker.GameFiles;
using CwTexture = CodeWalker.GameFiles.Texture;

namespace S4ClothesRenderer;

public sealed class ContentIndex
{
    private readonly Dictionary<string, (FileRef File, int Rank)> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, (FileRef File, int Rank)> ytds = [];
    public readonly List<FileRef> VehicleMetas = [];
    public readonly List<FileRef> WeaponMetas = [];
    public readonly List<FileRef> Gtxds = [];
    public readonly List<FileRef> Ytyps = [];
    public readonly List<FileRef> CarCols = [];
    public readonly List<FileRef> CarVariations = [];

    public void Add(string fileName, FileRef file, int rank)
    {
        var name = fileName.ToLowerInvariant();
        var ext = Path.GetExtension(name);
        switch (ext)
        {
            case ".ydr":
            case ".ydd":
            case ".yft":
                Put(files, name, file, rank);
                break;
            case ".ytd":
                Put(files, name, file, rank);
                Put(ytds, JenkHash.GenHash(name[..^4]), file, rank);
                break;
            case ".ytyp":
                Ytyps.Add(file);
                break;
            case ".meta":
                if (name.StartsWith("vehicles")) VehicleMetas.Add(file);
                else if (name.StartsWith("weapon")) WeaponMetas.Add(file);
                else if (name == "gtxd.meta") Gtxds.Add(file);
                else if (name.StartsWith("carcols")) CarCols.Add(file);
                else if (name.StartsWith("carvariations")) CarVariations.Add(file);
                break;
            case ".ymt":
                if (name.EndsWith("gtxd.ymt")) Gtxds.Add(file);
                else if (name.StartsWith("carcols")) CarCols.Add(file);
                else if (name.StartsWith("carvariations")) CarVariations.Add(file);
                break;
        }
    }

    private static void Put<TKey>(Dictionary<TKey, (FileRef, int)> map, TKey key, FileRef file, int rank)
    {
        if (!map.TryGetValue(key, out var old) || old.Item2 <= rank) map[key] = (file, rank);
    }

    public FileRef Get(string fileName) => files.TryGetValue(fileName, out var hit) ? hit.File : null;

    public FileRef Ytd(uint hash) => ytds.TryGetValue(hash, out var hit) ? hit.File : null;

    public IEnumerable<string> Names(string ext) => files.Keys.Where(k => k.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}

public sealed record VehicleDef(string Model, string Txd, string Class, string Label);

public sealed record WeaponDef(string Name, string Model);

public sealed class Library
{
    private readonly ContentIndex[] indexes;
    private readonly ConcurrentDictionary<uint, YtdFile> sharedYtds = new();
    private readonly ConditionalWeakTable<CwTexture, Tex> decoded = new();
    private readonly object archetypeLock = new();
    private Dictionary<uint, uint> archetypeTxds;
    public Dictionary<uint, uint> Parents { get; } = [];
    public List<VehicleDef> Vehicles { get; } = [];
    public List<WeaponDef> Weapons { get; } = [];
    public uint[] PaintColors { get; private set; } = [];
    public Dictionary<string, List<(byte Primary, byte Secondary)>> PaintCombos { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Library(params ContentIndex[] indexesByPriority)
    {
        indexes = [.. indexesByPriority.Where(i => i != null)];
        foreach (var index in indexes.Reverse())
        {
            foreach (var file in index.Gtxds) ReadGtxd(file);
            foreach (var file in index.VehicleMetas) ReadVehicles(file);
            foreach (var file in index.WeaponMetas) ReadWeapons(file);
            foreach (var file in index.CarCols) ReadCarCols(file);
            foreach (var file in index.CarVariations) ReadCarVariations(file);
        }
        DedupeByModel(Vehicles, v => v.Model);
        DedupeByModel(Weapons, w => w.Name);
    }

    private static void DedupeByModel<T>(List<T> list, Func<T, string> key)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!seen.Add(key(list[i]))) list.RemoveAt(i);
        }
    }

    public FileRef Get(string fileName)
    {
        foreach (var index in indexes)
        {
            var hit = index.Get(fileName);
            if (hit != null) return hit;
        }
        return null;
    }

    private FileRef YtdFileRef(uint hash)
    {
        foreach (var index in indexes)
        {
            var hit = index.Ytd(hash);
            if (hit != null) return hit;
        }
        return null;
    }

    public IEnumerable<string> Names(string ext) => indexes.SelectMany(i => i.Names(ext)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static XDocument Xml(FileRef file)
    {
        try
        {
            var bytes = file.Bytes();
            if (bytes == null || bytes.Length == 0 || bytes[0] == 'R' && bytes.Length > 3 && bytes[1] == 'B' && bytes[2] == 'F') return null;
            var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿', '\0', ' ', '\r', '\n', '\t');
            return XDocument.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    private static string Value(XElement e, string name) => e.Element(name)?.Value?.Trim() ?? "";

    private void ReadVehicles(FileRef file)
    {
        var doc = Xml(file);
        if (doc?.Root == null) return;
        foreach (var item in doc.Root.Element("InitDatas")?.Elements("Item") ?? [])
        {
            var model = Value(item, "modelName").ToLowerInvariant();
            if (model == "") continue;
            var txd = Value(item, "txdName").ToLowerInvariant();
            Vehicles.Add(new VehicleDef(model, txd == "" ? model : txd, Value(item, "vehicleClass").ToUpperInvariant(), Value(item, "gameName")));
        }
        foreach (var item in doc.Root.Element("txdRelationships")?.Elements("Item") ?? [])
        {
            var parent = Value(item, "parent").ToLowerInvariant();
            var child = Value(item, "child").ToLowerInvariant();
            if (parent != "" && child != "") Parents.TryAdd(JenkHash.GenHash(child), JenkHash.GenHash(parent));
        }
    }

    private void ReadWeapons(FileRef file)
    {
        var doc = Xml(file);
        if (doc?.Root == null) return;
        foreach (var item in doc.Descendants("Item").Where(i => (string)i.Attribute("type") == "CWeaponInfo"))
        {
            var name = Value(item, "Name");
            var model = Value(item, "Model").ToLowerInvariant();
            if (name == "" || model == "" || !name.StartsWith("WEAPON_", StringComparison.OrdinalIgnoreCase)) continue;
            Weapons.Add(new WeaponDef(name.ToUpperInvariant(), model));
        }
    }

    private static RpfFileEntry EntryOf(FileRef file) =>
        file.Entry ?? new RpfBinaryFileEntry { Name = Path.GetFileName(file.Path), NameLower = Path.GetFileName(file.Path).ToLowerInvariant() };

    private void ReadCarCols(FileRef file)
    {
        try
        {
            var carcols = new CarColsFile();
            carcols.Load(file.Bytes(), EntryOf(file));
            var colors = carcols.VehicleModelInfo?.Colors;
            if (colors != null && colors.Length > PaintColors.Length) PaintColors = [.. colors.Select(c => c.color)];
        }
        catch
        {
        }
    }

    private void ReadCarVariations(FileRef file)
    {
        try
        {
            var variations = new CarVariationsFile();
            variations.Load(file.Bytes(), EntryOf(file));
            foreach (var v in variations.VehicleModelInfo?.variationData ?? [])
            {
                if (string.IsNullOrEmpty(v.modelName) || v.colors == null) continue;
                var combos = v.colors.Where(c => c.indices != null && c.indices.Length >= 2).Select(c => (c.indices[0], c.indices[1])).Distinct().ToList();
                if (combos.Count > 0) PaintCombos[v.modelName] = combos;
            }
        }
        catch
        {
        }
    }

    public float[] Paint(byte index)
    {
        if (index >= PaintColors.Length) return null;
        var c = PaintColors[index];
        return [(c >> 16) & 255, (c >> 8) & 255, c & 255];
    }

    private void ReadGtxd(FileRef file)
    {
        try
        {
            var gtxd = new GtxdFile();
            gtxd.Load(file.Bytes(), file.Entry ?? new RpfBinaryFileEntry { Name = Path.GetFileName(file.Path), NameLower = Path.GetFileName(file.Path).ToLowerInvariant() });
            foreach (var kv in gtxd.TxdRelationships ?? [])
            {
                Parents.TryAdd(JenkHash.GenHash(kv.Key.ToLowerInvariant()), JenkHash.GenHash(kv.Value.ToLowerInvariant()));
            }
        }
        catch
        {
        }
    }

    private Dictionary<uint, uint> ArchetypeTxds()
    {
        lock (archetypeLock)
        {
            if (archetypeTxds != null) return archetypeTxds;
            var map = new Dictionary<uint, uint>();
            foreach (var index in indexes.Reverse())
            {
                foreach (var file in index.Ytyps)
                {
                    try
                    {
                        var ytyp = new YtypFile();
                        if (file.Entry != null) ytyp.Load(file.Bytes(), file.Entry);
                        else ytyp.Load(file.Bytes());
                        foreach (var archetype in ytyp.AllArchetypes ?? [])
                        {
                            if (archetype.TextureDict.Hash != 0) map[archetype.Hash.Hash] = archetype.TextureDict.Hash;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            return archetypeTxds = map;
        }
    }

    public uint ArchetypeTxd(string model) => ArchetypeTxds().TryGetValue(JenkHash.GenHash(model), out var txd) ? txd : 0;

    private YtdFile LoadYtd(uint hash, bool shared)
    {
        if (shared && sharedYtds.TryGetValue(hash, out var cached)) return cached;
        var file = YtdFileRef(hash);
        if (file == null) return null;
        YtdFile ytd;
        try
        {
            ytd = file.LoadTextures();
        }
        catch
        {
            return null;
        }
        if (shared)
        {
            if (sharedYtds.Count > 256) sharedYtds.Clear();
            sharedYtds[hash] = ytd;
        }
        return ytd;
    }

    public void ResolveTextures(List<Mesh> meshes, IEnumerable<uint> txdHashes, int maxSize)
    {
        var own = txdHashes.Where(h => h != 0).Distinct().ToList();
        var loaded = new Dictionary<uint, YtdFile>();
        YtdFile Own(uint hash)
        {
            if (!loaded.TryGetValue(hash, out var ytd)) loaded[hash] = ytd = LoadYtd(hash, false);
            return ytd;
        }
        CwTexture Find(CwTexture embedded, uint hash)
        {
            if (embedded != null || hash == 0) return embedded;
            foreach (var txd in own)
            {
                var texture = Own(txd)?.TextureDict?.Lookup(hash);
                if (texture != null) return texture;
            }
            foreach (var txd in own)
            {
                var parent = txd;
                for (var depth = 0; depth < 8 && Parents.TryGetValue(parent, out parent); depth++)
                {
                    var texture = LoadYtd(parent, true)?.TextureDict?.Lookup(hash);
                    if (texture != null) return texture;
                }
            }
            return null;
        }
        Tex Decode(CwTexture texture)
        {
            if (texture == null) return null;
            var tex = decoded.GetValue(texture, t => Assets3D.Decode(t, maxSize) ?? new Tex());
            return tex.Rgba == null ? null : tex;
        }
        foreach (var mesh in meshes)
        {
            if (mesh.Tint != 0) mesh.PaletteTex = Decode(Find(mesh.Palette, mesh.PaletteHash));
            if (mesh.Tex != null || mesh.Surface == Surface.Glass) continue;
            mesh.Tex = Decode(Find(mesh.Embedded, mesh.Diffuse));
        }
    }
}
