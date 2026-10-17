using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;

namespace S4ClothesRenderer;

public sealed record Tattoo(int Zone, float X, float Y, float ScaleX, float ScaleY, float Rotation, string Txd, string Texture);

public static class Skin
{
    private const int MaxTexture = 1024;
    private const int DecalSize = 512;
    private const int EyeColors = 32;
    public static readonly string[] Kinds = ["face", "tattoos"];
    private static readonly (string Tag, string Group)[] Overlays =
    [
        ("facialHair", "facial_hair"), ("eyebrow", "eyebrows"), ("blemishes", "blemishes"), ("aging", "ageing"), ("makeup", "makeup"),
        ("blusher", "blush"), ("baseDetail", "complexion"), ("damage", "sun_damage"), ("skinDetail1", "lipstick"), ("skinDetail2", "moles_freckles"),
    ];
    private static readonly string[] ShadeGroups = ["blemishes", "ageing", "complexion", "sun_damage", "moles_freckles"];
    private static readonly string[] FemaleGroups = ["makeup", "blush", "lipstick"];
    private static readonly string[] Zones = ["ZONE_TORSO", "ZONE_HEAD", "ZONE_LEFT_ARM", "ZONE_RIGHT_ARM", "ZONE_LEFT_LEG", "ZONE_RIGHT_LEG"];
    private static readonly Dictionary<string, float[]> Tints = new()
    {
        ["facial_hair"] = [38, 25, 17], ["eyebrows"] = [38, 25, 17], ["blush"] = [196, 84, 96], ["lipstick"] = [150, 32, 44],
    };
    private static readonly Camera[] Views = [Camera.Toward(0, -1, 0), Camera.Toward(0, 1, 0), Camera.Toward(1, 0, 0), Camera.Toward(-1, 0, 0)];
    private static readonly ConcurrentDictionary<string, Tattoo> tattoos = new();
    private static readonly ConcurrentDictionary<string, List<(string Comp, Mesh Mesh)>> bodies = new();
    private static readonly ConcurrentDictionary<string, Tex> textures = new();

    public static List<ObjectItem> Catalog(GameArchive game, Library lib)
    {
        var items = new List<ObjectItem>();
        if (game == null) return items;
        var meta = lib.Get("facial_overlays.meta");
        if (meta != null)
        {
            var root = Xml(meta).Root;
            foreach (var (tag, group) in Overlays)
            {
                var sex = FemaleGroups.Contains(group) ? "female" : "male";
                var names = root?.Element(tag)?.Elements().Select(e => e.Value.Trim()).ToList() ?? [];
                for (var i = 0; i < names.Count; i++)
                {
                    if (lib.Get(names[i].ToLowerInvariant() + ".ytd") == null) continue;
                    items.Add(new ObjectItem("face", group, group + "_" + i, names[i], names[i], null, null, sex));
                }
            }
        }
        if (lib.Get("mp_eye_colour.ytd") != null)
        {
            for (var i = 0; i < EyeColors; i++) items.Add(new ObjectItem("face", "eyes", "eyes_" + i, i.ToString(), "mp_eye_colour", null, null, "male"));
        }
        foreach (var file in lib.Names("_overlays.xml"))
        {
            var collection = file[..^"_overlays.xml".Length];
            if (collection == "singleplayer") continue;
            foreach (var item in Xml(lib.Get(file)).Root?.Element("presets")?.Elements() ?? [])
            {
                if (item.Element("type")?.Value != "TYPE_TATTOO") continue;
                var zone = Array.IndexOf(Zones, item.Element("zone")?.Value);
                var name = item.Element("nameHash")?.Value;
                var txd = item.Element("txdHash")?.Value;
                if (zone < 0 || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(txd) || lib.Get(txd.ToLowerInvariant() + ".ytd") == null) continue;
                var pos = item.Element("uvPos");
                var scale = item.Element("scale");
                tattoos[collection + "/" + name] = new Tattoo(zone, Attr(pos, "x"), Attr(pos, "y"), Attr(scale, "x"), Attr(scale, "y"), Attr(item.Element("rotation"), "value"), txd, item.Element("txtHash")?.Value ?? txd);
                var sex = item.Element("gender")?.Value == "GENDER_FEMALE" ? "female" : "male";
                items.Add(new ObjectItem("tattoos", collection, name, txd, txd, txd, null, sex));
            }
        }
        return items;
    }

    private static XDocument Xml(FileRef file) => XDocument.Parse(Encoding.UTF8.GetString(file.Bytes()));

    private static float Attr(XElement e, string name) => float.TryParse(e?.Attribute(name)?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static byte[] Render(GameArchive game, Library lib, ObjectItem item, int size, RenderBuffers buffers)
    {
        return item.Kind == "face" ? RenderFace(game, lib, item, size, buffers) : RenderTattoo(game, lib, item, size, buffers);
    }

    private static byte[] RenderFace(GameArchive game, Library lib, ObjectItem item, int size, RenderBuffers buffers)
    {
        var head = Body(game, item.Sex, true).Select(p => p.Mesh).ToList();
        if (head.Count == 0) throw new InvalidDataException("no head");
        var skin = head[0].Tex;
        var tex = new Tex { Width = skin.Width, Height = skin.Height, Rgba = (byte[])skin.Rgba.Clone(), UseAlpha = false };
        var (ex0, ey0, ex1, ey1) = EyeRegion(skin);
        if (item.Group == "eyes")
        {
            var atlas = Texture(lib, "mp_eye_colour", "mp_eye_colour");
            var index = int.Parse(item.Model);
            int tw = atlas.Width / 8, th = atlas.Height / 4;
            Paste(tex, atlas, index % 8 * tw, index / 8 * th, tw, th, (int)(ex0 * tex.Width), (int)(ey0 * tex.Height), (int)((ex1 - ex0) * tex.Width), (int)((ey1 - ey0) * tex.Height));
        }
        else
        {
            var overlay = Texture(lib, item.Model, item.Model);
            var tint = Tints.GetValueOrDefault(item.Group);
            var shade = ShadeGroups.Contains(item.Group);
            for (var y = 0; y < tex.Height; y++)
            {
                var oy = Math.Min(overlay.Height - 1, y * overlay.Height / tex.Height);
                for (var x = 0; x < tex.Width; x++)
                {
                    var o = (oy * overlay.Width + Math.Min(overlay.Width - 1, x * overlay.Width / tex.Width)) * 4;
                    var a = overlay.Rgba[o + 3] / 255f;
                    if (a <= 0) continue;
                    var p = (y * tex.Width + x) * 4;
                    for (var k = 0; k < 3; k++) tex.Rgba[p + k] = shade ? (byte)Math.Min(255, tex.Rgba[p + k] * (1 - a + a * overlay.Rgba[o + k] / 128f)) : (byte)(tex.Rgba[p + k] * (1 - a) + (tint?[k] ?? overlay.Rgba[o + k]) * a);
                }
            }
        }
        var meshes = head.Select(m => { var c = m.Copy(); c.Tex = tex; return c; }).ToList();
        var cam = Camera.Toward(0, -1, 0.08f);
        if (item.Group != "eyes") return Renderer.Render(meshes, null, cam, size, false, buffers);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        var top = head.SelectMany(m => m.Positions.Where((_, i) => i % 3 == 2)).Max();
        var bottom = head.SelectMany(m => m.Positions.Where((_, i) => i % 3 == 2)).Min();
        foreach (var m in head)
        {
            for (var i = 0; i < m.Positions.Length / 3; i++)
            {
                if (m.Uvs[i * 2] < ex0 || m.Uvs[i * 2 + 1] < ey0 || m.Positions[i * 3 + 2] < (top + bottom) / 2) continue;
                var (sx, sy, _) = cam.Project(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx); minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
            }
        }
        if (minX > maxX) return Renderer.Render(meshes, null, cam, size, false, buffers);
        return Renderer.RenderFixed(meshes, cam, size, (minX + maxX) / 2, (minY + maxY) / 2, (maxX - minX) * 1.15f, buffers);
    }

    private static (float, float, float, float) EyeRegion(Tex skin)
    {
        int w = skin.Width, h = skin.Height, x0 = w, y0 = h, x1 = 0, y1 = 0;
        var bg = ((int)(h * 0.74f) * w + (int)(w * 0.74f)) * 4;
        for (var y = (int)(h * 0.7f); y < h; y++)
        {
            for (var x = (int)(w * 0.7f); x < w; x++)
            {
                var p = (y * w + x) * 4;
                if (Math.Abs(skin.Rgba[p] - skin.Rgba[bg]) + Math.Abs(skin.Rgba[p + 1] - skin.Rgba[bg + 1]) + Math.Abs(skin.Rgba[p + 2] - skin.Rgba[bg + 2]) <= 60) continue;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
        }
        return x1 > x0 ? ((float)x0 / w, (float)y0 / h, (x1 + 1f) / w, (y1 + 1f) / h) : (0.78f, 0.78f, 0.98f, 0.98f);
    }

    private static void Paste(Tex dst, Tex src, int sx, int sy, int sw, int sh, int dx, int dy, int dw, int dh)
    {
        for (var y = 0; y < dh; y++)
        {
            var ty = dy + y;
            if (ty < 0 || ty >= dst.Height) continue;
            var oy = sy + Math.Min(sh - 1, y * sh / dh);
            for (var x = 0; x < dw; x++)
            {
                var tx = dx + x;
                if (tx < 0 || tx >= dst.Width) continue;
                var o = (oy * src.Width + sx + Math.Min(sw - 1, x * sw / dw)) * 4;
                var a = src.Rgba[o + 3] / 255f;
                var p = (ty * dst.Width + tx) * 4;
                for (var k = 0; k < 3; k++) dst.Rgba[p + k] = (byte)(dst.Rgba[p + k] * (1 - a) + src.Rgba[o + k] * a);
            }
        }
    }

    private static byte[] RenderTattoo(GameArchive game, Library lib, ObjectItem item, int size, RenderBuffers buffers)
    {
        if (!tattoos.TryGetValue(item.Group + "/" + item.Name, out var tattoo)) throw new InvalidDataException("unknown tattoo");
        var parts = Body(game, item.Sex, false);
        var body = parts.Select(p => p.Mesh).ToList();
        if (body.Count == 0) throw new InvalidDataException("no body");
        var art = Texture(lib, tattoo.Txd, tattoo.Texture);
        var decal = new Tex { Width = DecalSize, Height = DecalSize, Rgba = new byte[DecalSize * DecalSize * 4], UseAlpha = true };
        var angle = tattoo.Rotation * MathF.PI / 180;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        for (var y = 0; y < DecalSize; y++)
        {
            for (var x = 0; x < DecalSize; x++)
            {
                float du = (x + 0.5f) / DecalSize - tattoo.X, dv = (y + 0.5f) / DecalSize - (1 - tattoo.Y);
                var tu = (du * cos + dv * sin) / Math.Max(tattoo.ScaleX, 1e-3f) + 0.5f;
                var tv = (-du * sin + dv * cos) / Math.Max(tattoo.ScaleY, 1e-3f) + 0.5f;
                if (tu < 0 || tu >= 1 || tv < 0 || tv >= 1) continue;
                var o = ((int)(tv * art.Height) * art.Width + (int)(tu * art.Width)) * 4;
                Array.Copy(art.Rgba, o, decal.Rgba, (y * DecalSize + x) * 4, 4);
            }
        }
        var meshes = parts.Select(p =>
        {
            var c = p.Mesh.Copy();
            if (p.Comp == "lowr" ? tattoo.Zone >= 4 : p.Comp == "head" ? tattoo.Zone == 1 : tattoo.Zone <= 3) (c.Decal, c.DecalZone) = (decal, tattoo.Zone);
            return c;
        }).ToList();
        var best = Views[0];
        var bestCount = -1;
        var (cx, cy, span) = (0f, 0f, 0f);
        foreach (var cam in Views)
        {
            var frame = ZoneFrame(body, tattoo.Zone, cam);
            Renderer.RenderFixed(meshes, cam, 96, frame.X, frame.Y, frame.Span, buffers);
            if (buffers.DecalPixels <= bestCount) continue;
            (best, bestCount, (cx, cy, span)) = (cam, buffers.DecalPixels, frame);
        }
        return Renderer.RenderFixed(meshes, best, size, cx, cy, span, buffers);
    }

    private static (float X, float Y, float Span) ZoneFrame(List<Mesh> body, int zone, Camera cam)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var m in body)
        {
            if (m.Uv1 == null) continue;
            for (var i = 0; i < m.Positions.Length / 3; i++)
            {
                if ((int)MathF.Floor(m.Uv1[i * 2 + 1]) != -(zone * 2 + 1)) continue;
                var (sx, sy, _) = cam.Project(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx); minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
            }
        }
        if (minX > maxX) return (0, 0, 2);
        return ((minX + maxX) / 2, (minY + maxY) / 2, Math.Max(maxX - minX, maxY - minY) * 1.2f);
    }

    private static Tex Texture(Library lib, string txd, string name)
    {
        return textures.GetOrAdd(txd.ToLowerInvariant() + "/" + name.ToLowerInvariant(), _ =>
        {
            var ytd = (lib.Get(txd.ToLowerInvariant() + ".ytd") ?? throw new FileNotFoundException(txd)).LoadTextures();
            var list = ytd.TextureDict?.Textures?.data_items ?? [];
            var texture = list.FirstOrDefault(t => string.Equals(t?.Name, name, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault(t => t != null);
            return Assets3D.Decode(texture, MaxTexture) ?? throw new InvalidDataException(name);
        });
    }

    private static List<(string Comp, Mesh Mesh)> Body(GameArchive game, string sex, bool headOnly)
    {
        return bodies.GetOrAdd(game.Folder + "|" + sex + "|" + headOnly, _ =>
        {
            var prefix = sex == "male" ? "mp_m_freemode_01^" : "mp_f_freemode_01^";
            (string, string)[] parts = headOnly ? [("head", "000")] : [("head", "000"), ("uppr", "015"), ("lowr", sex == "male" ? "014" : "015")];
            var meshes = new List<(string, Mesh)>();
            foreach (var (comp, num) in parts)
            {
                var model = game.Files.FirstOrDefault(f => f.FullName.StartsWith(prefix + comp + "_" + num + "_", StringComparison.Ordinal) && f.FullName.EndsWith(".ydd")).File;
                var texture = (game.Files.FirstOrDefault(f => f.FullName == prefix + comp + "_diff_" + num + "_a_whi.ytd").File
                    ?? game.Files.FirstOrDefault(f => f.FullName.StartsWith(prefix + comp + "_diff_" + num + "_a", StringComparison.Ordinal)).File);
                if (model == null || texture == null) continue;
                var tex = Assets3D.ReadTexture(texture.LoadTextures(), MaxTexture);
                if (tex == null) continue;
                var part = Assets3D.ReadMeshes(model.LoadDrawables());
                foreach (var m in part)
                {
                    m.Tex = tex;
                    meshes.Add((comp, m));
                }
            }
            return meshes;
        });
    }
}
