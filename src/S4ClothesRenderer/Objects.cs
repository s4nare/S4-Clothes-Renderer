using CodeWalker.GameFiles;
using SharpDX;

namespace S4ClothesRenderer;

public sealed record ObjectItem(string Kind, string Group, string Name, string Model, string Label, string Txd);

public static class Objects
{
    public static readonly string[] Kinds = ["vehicles", "weapons", "props", "peds"];
    private static readonly string[] PedComponents = ["head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib"];
    private static readonly ushort[] FrontWheels = [27922, 26418];
    private static readonly ushort[] RearWheels = [29921, 29922, 29923, 27902, 5857, 5858, 5859, 26398];
    private static readonly ushort[] RightWheels = [26418, 5857, 5858, 5859, 26398];
    private static readonly Dictionary<string, string> WeaponGroups = new()
    {
        ["pi"] = "pistols", ["sb"] = "smgs", ["ar"] = "rifles", ["sg"] = "shotguns", ["mg"] = "mgs",
        ["sr"] = "snipers", ["lr"] = "heavy", ["ex"] = "throwables", ["me"] = "melee",
    };

    public static Camera CameraFor(string kind) => kind switch
    {
        "vehicles" => Camera.Toward(-0.85f, 1f, 0.42f),
        "weapons" => Camera.Toward(0f, -1f, 0.08f),
        "peds" => Camera.Of(View.Front),
        _ => Camera.Toward(0.55f, -1f, 0.5f),
    };

    public static List<ObjectItem> Build(Library lib)
    {
        var items = new List<ObjectItem>();
        foreach (var v in lib.Vehicles)
        {
            var yft = lib.Get(v.Model + ".yft");
            if (yft == null || yft.Entry?.FileSize < 2048) continue;
            var group = v.Class.StartsWith("VC_") ? v.Class[3..].ToLowerInvariant().Replace("compacts", "compact") : "other";
            items.Add(new ObjectItem("vehicles", group, v.Model, v.Model, v.Label, v.Txd));
        }
        foreach (var w in lib.Weapons)
        {
            if (lib.Get(w.Model + ".ydr") == null && lib.Get(w.Model + ".yft") == null) continue;
            var parts = w.Model.Split('_');
            var group = parts.Length > 1 && WeaponGroups.TryGetValue(parts[1], out var g) ? g : "other";
            items.Add(new ObjectItem("weapons", group, w.Name, w.Model, w.Name, w.Model));
        }
        var peds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in lib.Names(".yft").Select(n => n[..^4]))
        {
            if (name.Contains("freemode") || name.StartsWith("slod") || lib.Get(name + ".ydd") == null || lib.Get(name + ".ytd") == null) continue;
            peds.Add(name);
            var group = name.StartsWith("a_c_") ? "animals" : name.Split('_')[0];
            items.Add(new ObjectItem("peds", group, name, name, name, name));
        }
        var vehicles = new HashSet<string>(lib.Vehicles.Select(v => v.Model), StringComparer.OrdinalIgnoreCase);
        var props = lib.Names(".ydr").Concat(lib.Names(".yft")).Select(n => n[..^4]).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in props)
        {
            if (!(name.StartsWith("prop_") || name.StartsWith("p_")) || name.EndsWith("_lod") || name.EndsWith("_slod") || name.EndsWith("_hi") || peds.Contains(name) || vehicles.Contains(name)) continue;
            var rest = name.StartsWith("prop_") ? name[5..] : name[2..];
            var first = rest.Length > 0 && char.IsLetter(rest[0]) ? rest[0].ToString() : "0-9";
            items.Add(new ObjectItem("props", first, name, name, name, name));
        }
        return items;
    }

    public static List<Mesh> Scene(ObjectItem item, Library lib, int maxTexture)
    {
        var meshes = new List<Mesh>();
        var txds = new List<string>();
        var txdHashes = new List<uint>();
        switch (item.Kind)
        {
            case "vehicles":
                AddFragment(meshes, lib.Get(item.Model + ".yft").LoadFragment().Fragment);
                txds.AddRange([item.Model + "+hi", item.Txd, item.Model]);
                break;
            case "peds":
                AddPed(meshes, lib, item.Model, maxTexture);
                txds.Add(item.Model);
                break;
            default:
                var ydr = lib.Get(item.Model + ".ydr");
                if (ydr != null) Assets3D.AddDrawable(meshes, ydr.LoadDrawable().Drawable, null, true);
                else AddFragment(meshes, lib.Get(item.Model + ".yft").LoadFragment().Fragment);
                txds.Add(item.Txd);
                var archetype = lib.ArchetypeTxd(item.Model);
                if (archetype != 0) txdHashes.Add(archetype);
                break;
        }
        lib.ResolveTextures(meshes, txds.Where(n => !string.IsNullOrEmpty(n)).Select(n => JenkHash.GenHash(n.ToLowerInvariant())).Concat(txdHashes), maxTexture);
        return meshes;
    }

    private static void AddFragment(List<Mesh> meshes, FragType frag)
    {
        if (frag?.Drawable == null) return;
        var embedded = frag.Drawable.ShaderGroup?.TextureDictionary;
        Assets3D.AddDrawable(meshes, frag.Drawable, null, true, embedded);
        if (frag.DrawableCloth != null) Assets3D.AddDrawable(meshes, frag.DrawableCloth, null, true, embedded);
        var lod = frag.PhysicsLODGroup?.PhysicsLOD1;
        var children = lod?.Children?.data_items;
        if (children == null) return;
        var matrices = lod.FragTransforms?.Matrices;
        var offset = lod.PositionOffset;
        FragDrawable front = null, rear = null;
        foreach (var child in children)
        {
            if (child?.Drawable1?.AllModels == null || child.Drawable1.AllModels.Length == 0) continue;
            if (FrontWheels.Contains(child.BoneTag)) front ??= child.Drawable1;
            else if (RearWheels.Contains(child.BoneTag)) rear ??= child.Drawable1;
        }
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            if (child == null || matrices == null || i >= matrices.Length) continue;
            var drawable = child.Drawable1;
            if (FrontWheels.Contains(child.BoneTag)) drawable = front ?? rear;
            else if (RearWheels.Contains(child.BoneTag)) drawable = rear ?? front;
            if (drawable?.AllModels == null || drawable.AllModels.Length == 0) continue;
            var m = matrices[i];
            if (RightWheels.Contains(child.BoneTag))
            {
                m.M11 = -1; m.M12 = 0; m.M13 = 0;
                m.M21 = 0; m.M22 = 1; m.M23 = 0;
                m.M31 = 0; m.M32 = 0; m.M33 = -1;
            }
            m.Row4 += new Vector4(offset, 0);
            m.Column4 = Vector4.UnitW;
            Assets3D.AddDrawable(meshes, drawable, m, false, embedded);
        }
    }

    private static void AddPed(List<Mesh> meshes, Library lib, string model, int maxTexture)
    {
        var ydd = lib.Get(model + ".ydd").LoadDrawables();
        var dict = ydd.Dict;
        if (dict == null) return;
        var ytd = lib.Get(model + ".ytd")?.LoadTextures();
        var textures = ytd?.TextureDict?.Textures?.data_items ?? [];
        foreach (var comp in PedComponents)
        {
            Drawable drawable = null;
            foreach (var suffix in new[] { "_u", "_r", "_m", "" })
            {
                if (dict.TryGetValue(JenkHash.GenHash(comp + "_000" + suffix), out drawable)) break;
            }
            if (drawable == null) continue;
            var start = meshes.Count;
            Assets3D.AddDrawable(meshes, drawable, null, false);
            var fallback = textures.FirstOrDefault(t => t?.Name != null && t.Name.StartsWith(comp + "_diff_000_a", StringComparison.OrdinalIgnoreCase));
            for (var i = start; i < meshes.Count; i++)
            {
                var mesh = meshes[i];
                if (mesh.Embedded == null && mesh.Diffuse != 0) mesh.Embedded = ytd?.TextureDict?.Lookup(mesh.Diffuse);
                mesh.Embedded ??= fallback;
            }
        }
    }
}
