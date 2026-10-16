using CodeWalker.GameFiles;
using SharpDX;
using CwTexture = CodeWalker.GameFiles.Texture;

namespace S4ClothesRenderer;

public sealed class Mesh
{
    public float[] Positions;
    public float[] Normals;
    public float[] Uvs;
    public int[] Indices;
    public bool HasNormals;
    public bool Hair;
    public bool AlphaTest;
    public uint Diffuse;
    public CwTexture Embedded;
    public Surface Surface;
    public Tex Tex;
}

public enum Surface
{
    Textured,
    Paint,
    Glass,
}

public sealed class Tex
{
    public int Width;
    public int Height;
    public byte[] Rgba;
    public bool UseAlpha;
}

public static class Assets3D
{
    private static readonly HashSet<uint> HairShaders = Hashes("ped_hair_cutout_alpha", "ped_hair_spiked");

    private static readonly HashSet<uint> PaintShaders = Hashes([.. Enumerable.Range(1, 9).SelectMany(n => new[] { "", "_enveff", "_lvr", "_emissive" }.Select(s => "vehicle_paint" + n + s))]);
    private static readonly HashSet<uint> GlassShaders = Hashes("vehicle_vehglass", "glass", "glass_pv", "glass_pv_env", "glass_env", "glass_spec", "glass_reflect", "glass_breakable", "glass_breakable_screendooralpha", "glass_displacement", "glass_emissive", "glass_emissivenight", "glass_emissive_alpha", "glass_emissivenight_alpha");
    private static readonly HashSet<uint> HiddenShaders = Hashes("vehicle_vehglass_inner", "vehicle_shadow", "shadow_proxy", "vehicle_blurredrotor");
    private const uint DiffuseSampler = 4059966321;

    private static HashSet<uint> Hashes(params string[] names) =>
        [.. names.SelectMany(n => new[] { JenkHash.GenHash(n), JenkHash.GenHash(n + ".sps") })];

    private static bool Is(HashSet<uint> set, ShaderFX shader) => shader != null && (set.Contains(shader.Name.Hash) || set.Contains(shader.FileName.Hash));

    public static List<Mesh> ReadMeshes(YddFile ydd)
    {
        var meshes = new List<Mesh>();
        foreach (var drawable in ydd.Drawables ?? ydd.DrawableDict?.Drawables?.data_items ?? []) AddDrawable(meshes, drawable, null, false);
        return meshes;
    }

    public static void AddDrawable(List<Mesh> meshes, DrawableBase drawable, Matrix? fixedTransform, bool useBones, TextureDictionary embedded = null)
    {
        var models = drawable?.DrawableModels?.High ?? drawable?.DrawableModels?.Med ?? drawable?.DrawableModels?.Low;
        if (models == null) return;
        embedded ??= drawable.ShaderGroup?.TextureDictionary;
        var bones = drawable.Skeleton?.Bones?.Items;
        foreach (var model in models)
        {
            if (model?.Geometries == null) continue;
            Matrix? transform = fixedTransform;
            if (transform == null && useBones && model.HasSkin == 0 && bones != null && model.BoneIndex < bones.Length) transform = bones[model.BoneIndex].AnimTransform;
            foreach (var geometry in model.Geometries)
            {
                var mesh = ReadGeometry(geometry, transform, embedded);
                if (mesh != null) meshes.Add(mesh);
            }
        }
    }

    private static Mesh ReadGeometry(DrawableGeometry geometry, Matrix? transform, TextureDictionary embedded)
    {
        if (Is(HiddenShaders, geometry.Shader)) return null;
        var data = geometry.VertexData;
        var indices = geometry.IndexBuffer?.Indices;
        if (data?.Info == null || data.VertexBytes == null || indices == null) return null;
        var flags = data.Info.Flags;
        bool Has(int c) => ((flags >> c) & 1) != 0;
        if (!Has(0)) return null;
        var count = data.VertexCount;
        var mesh = new Mesh
        {
            Positions = new float[count * 3],
            Normals = new float[count * 3],
            Uvs = new float[count * 2],
            Indices = new int[indices.Length],
            HasNormals = Has(3),
            Hair = Is(HairShaders, geometry.Shader),
            AlphaTest = geometry.Shader == null || geometry.Shader.RenderBucket != 0,
            Surface = Is(PaintShaders, geometry.Shader) ? Surface.Paint : Is(GlassShaders, geometry.Shader) ? Surface.Glass : Surface.Textured,
        };
        var parameters = geometry.Shader?.ParametersList;
        if (parameters?.Hashes != null && parameters.Parameters != null)
        {
            for (var i = 0; i < parameters.Hashes.Length && i < parameters.Parameters.Length; i++)
            {
                if ((uint)parameters.Hashes[i] != DiffuseSampler) continue;
                var value = parameters.Parameters[i].Data;
                if (value is CwTexture direct && direct.Data?.FullData != null) mesh.Embedded = direct;
                if (value is TextureBase texture) mesh.Diffuse = texture.NameHash != 0 ? texture.NameHash : JenkHash.GenHash((texture.Name ?? "").ToLowerInvariant());
                break;
            }
        }
        if (mesh.Embedded == null && mesh.Diffuse != 0) mesh.Embedded = embedded?.Lookup(mesh.Diffuse);
        Span<float> v = stackalloc float[4];
        for (var i = 0; i < count; i++)
        {
            Read(data, i, 0, v);
            mesh.Positions[i * 3] = v[0]; mesh.Positions[i * 3 + 1] = v[1]; mesh.Positions[i * 3 + 2] = v[2];
            if (mesh.HasNormals)
            {
                Read(data, i, 3, v);
                var len = MathF.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
                if (len == 0) len = 1;
                mesh.Normals[i * 3] = v[0] / len; mesh.Normals[i * 3 + 1] = v[1] / len; mesh.Normals[i * 3 + 2] = v[2] / len;
            }
            if (Has(6))
            {
                Read(data, i, 6, v);
                mesh.Uvs[i * 2] = v[0]; mesh.Uvs[i * 2 + 1] = v[1];
            }
        }
        for (var i = 0; i < indices.Length; i++) mesh.Indices[i] = indices[i];
        if (transform is { } m && !m.IsIdentity) Transform(mesh, m);
        return mesh;
    }

    private static void Transform(Mesh mesh, Matrix m)
    {
        for (var i = 0; i < mesh.Positions.Length; i += 3)
        {
            var p = Vector3.TransformCoordinate(new Vector3(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2]), m);
            mesh.Positions[i] = p.X; mesh.Positions[i + 1] = p.Y; mesh.Positions[i + 2] = p.Z;
            if (!mesh.HasNormals) continue;
            var n = Vector3.Normalize(Vector3.TransformNormal(new Vector3(mesh.Normals[i], mesh.Normals[i + 1], mesh.Normals[i + 2]), m));
            mesh.Normals[i] = n.X; mesh.Normals[i + 1] = n.Y; mesh.Normals[i + 2] = n.Z;
        }
    }

    private static void Read(VertexData data, int vertex, int component, Span<float> v)
    {
        v.Clear();
        switch (data.Info.GetComponentType(component))
        {
            case VertexComponentType.Float:
                v[0] = data.GetFloat(vertex, component);
                break;
            case VertexComponentType.Float2:
                { var x = data.GetVector2(vertex, component); v[0] = x.X; v[1] = x.Y; }
                break;
            case VertexComponentType.Float3:
                { var x = data.GetVector3(vertex, component); v[0] = x.X; v[1] = x.Y; v[2] = x.Z; }
                break;
            case VertexComponentType.Float4:
                { var x = data.GetVector4(vertex, component); v[0] = x.X; v[1] = x.Y; v[2] = x.Z; v[3] = x.W; }
                break;
            case VertexComponentType.Half2:
                { var x = data.GetHalf2(vertex, component); v[0] = x.X; v[1] = x.Y; }
                break;
            case VertexComponentType.Half4:
                { var x = data.GetHalf4(vertex, component); v[0] = x.X; v[1] = x.Y; v[2] = x.Z; v[3] = x.W; }
                break;
            case VertexComponentType.RGBA8SNorm:
                { var x = data.GetRGBA8SNorm(vertex, component); v[0] = x.X; v[1] = x.Y; v[2] = x.Z; v[3] = x.W; }
                break;
            case VertexComponentType.Colour:
            case VertexComponentType.UByte4:
                { var x = data.GetColour(vertex, component); v[0] = x.R / 255f * 2 - 1; v[1] = x.G / 255f * 2 - 1; v[2] = x.B / 255f * 2 - 1; v[3] = x.A / 255f * 2 - 1; }
                break;
        }
    }

    public static Tex ReadTexture(YtdFile ytd, int maxSize) => Decode(ytd.TextureDict?.Textures?.data_items?.FirstOrDefault(t => t != null), maxSize);

    public static Tex Decode(CwTexture texture, int maxSize)
    {
        if (texture?.Data?.FullData == null) return null;
        var mip = 0;
        while (mip + 1 < texture.Levels && Math.Max(texture.Width >> mip, texture.Height >> mip) > maxSize) mip++;
        var width = Math.Max(1, texture.Width >> mip);
        var height = Math.Max(1, texture.Height >> mip);
        var rgba = texture.Format == TextureFormat.D3DFMT_BC7 ? Bc7.Decode(texture, mip, width, height) : FromBgra(CodeWalker.Utils.DDSIO.GetPixels(texture, mip));
        if (rgba == null || rgba.Length < width * height * 4) return null;
        var alphaMax = 0;
        for (var i = 3; i < rgba.Length; i += 4) if (rgba[i] > alphaMax) alphaMax = rgba[i];
        return new Tex { Width = width, Height = height, Rgba = rgba, UseAlpha = alphaMax > 16 };
    }

    private static byte[] FromBgra(byte[] bgra)
    {
        if (bgra == null) return null;
        for (var i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        return bgra;
    }
}

public static class Bc7
{
    private const string P2 = "0011001100110011000100010001000101110111011101110001001100110111000000010001001100110111011111110001001101111111000000010011011100000000000100110011011111111111000000010111111100000000000101110001011111111111000000001111111100001111111111110000000000001111000010001110111101110001000000000000000010001110011100110001000000110001000000000000100011001110000000001000110001110011001100010011000100010000000010001000110001100110011001100011011001101100000101111110100000001111111100000111000110001110001110011001110001010101010101010000111100001111010110100101101000110011110011000011110000111100010101011010101001101001011010010101101010100101011100111100111000010011110010000011001001001100001110111101110001101001100101100011110011000011011001101001100100000110011000000100111001000000001001110010000000000010011100100000010011100100011011001001001100110110110010010110001110011100001110011100011001101100110010010110001100111001011111101000000100011000111001110000111100110011001100111111000000100010111011100100010001110111";
    private const string P3 = "0011001102212222000100112211222100002001221122110222002200110111000000001122112200110011002200220022002211111111001100112211221100000000111122220000111111112222000011112222222200120012001200120112011201120112012201220122012200110112112212220011200122002220000100110112112201110011200122000000112211221122002200220022111101110111022202220001000122212221000000110122012200001100221022100122012200110000001200121122222201101221122101100000011012211221002211021102002201100110200222220011012201220011000020002211222100000002112212220222002200120011001100120022022201200120012001200000111122220000012012012012012001202012120101200011220011220011001111222200001101010101222222220000000021212121002211220022112200220011002200110220122102201221010122222222010100002121212121210101010101012222022201110222011100021112000211120000211221122112022201110111022200021112111200020110011001102222000000002112211201100110222222220022001100110022002211221122002200000000000021120002000100020001022212220222122201012222222222220111201122012220";
    private static readonly int[] A2 = [15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2, 15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6, 6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15];
    private static readonly int[] A31 = [3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3, 3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15, 8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15, 3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3];
    private static readonly int[] A32 = [15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8, 15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8, 15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8, 15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8];
    private static readonly int[][] Weights = [null, null, [0, 21, 43, 64], [0, 9, 18, 27, 37, 46, 55, 64], [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64]];
    private static readonly int[][] Modes =
    [
        [3, 4, 0, 0, 4, 0, 1, 0, 3, 0], [2, 6, 0, 0, 6, 0, 0, 1, 3, 0], [3, 6, 0, 0, 5, 0, 0, 0, 2, 0], [2, 6, 0, 0, 7, 0, 1, 0, 2, 0],
        [1, 0, 2, 1, 5, 6, 0, 0, 2, 3], [1, 0, 2, 0, 7, 8, 0, 0, 2, 2], [1, 0, 0, 0, 7, 7, 1, 0, 4, 0], [2, 6, 0, 0, 5, 5, 1, 0, 2, 0],
    ];

    public static byte[] Decode(CwTexture texture, int mip, int width, int height)
    {
        var data = texture.Data?.FullData;
        if (data == null) return null;
        long offset = 0;
        for (var level = 0; level < mip; level++)
        {
            offset += (long)Math.Max(1, ((texture.Width >> level) + 3) / 4) * Math.Max(1, ((texture.Height >> level) + 3) / 4) * 16;
        }
        var bw = Math.Max(1, (width + 3) / 4);
        var bh = Math.Max(1, (height + 3) / 4);
        var full = new byte[bw * 4 * bh * 4 * 4];
        for (var by = 0; by < bh; by++)
        {
            for (var bx = 0; bx < bw; bx++)
            {
                var o = offset + (by * bw + bx) * 16;
                if (o + 16 > data.Length) continue;
                Block(data, (int)o, full, bw * 4, bx, by);
            }
        }
        if (bw * 4 == width && bh * 4 == height) return full;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++) Buffer.BlockCopy(full, y * bw * 4 * 4, rgba, y * width * 4, width * 4);
        return rgba;
    }

    private static void Block(byte[] src, int off, byte[] output, int w, int bx, int by)
    {
        var pos = 0;
        int Bits(int n)
        {
            var v = 0;
            for (var i = 0; i < n; i++, pos++) v |= ((src[off + (pos >> 3)] >> (pos & 7)) & 1) << i;
            return v;
        }
        var mode = 0;
        while (mode < 8 && Bits(1) == 0) mode++;
        if (mode >= 8) return;
        var m = Modes[mode];
        int ns = m[0], cb = m[4], ab = m[5], ib = m[8], ib2 = m[9];
        var part = Bits(m[1]);
        var rotation = Bits(m[2]);
        var indexSel = Bits(m[3]);
        var ep = new int[ns * 2, 4];
        for (var e = 0; e < ns * 2; e++) ep[e, 3] = 255;
        for (var c = 0; c < 3; c++) for (var e = 0; e < ns * 2; e++) ep[e, c] = Bits(cb);
        if (ab > 0) for (var e = 0; e < ns * 2; e++) ep[e, 3] = Bits(ab);
        var pbits = new List<int>();
        if (m[6] == 1) for (var e = 0; e < ns * 2; e++) pbits.Add(Bits(1));
        if (m[7] == 1) for (var s = 0; s < ns; s++) { var p = Bits(1); pbits.Add(p); pbits.Add(p); }
        for (var e = 0; e < ns * 2; e++)
        {
            for (var c = 0; c < 4; c++)
            {
                if (c == 3 && ab == 0) continue;
                int v = ep[e, c], bits = c == 3 ? ab : cb;
                if (pbits.Count > 0) { v = (v << 1) | pbits[e]; bits++; }
                ep[e, c] = (v << (8 - bits)) | (v >> (2 * bits - 8));
            }
        }
        int SubsetOf(int i) => ns == 1 ? 0 : ns == 2 ? P2[part * 16 + i] - '0' : P3[part * 16 + i] - '0';
        bool IsAnchor(int i) => i == 0 || (ns == 2 && i == A2[part]) || (ns == 3 && (i == A31[part] || i == A32[part]));
        Span<int> idx = stackalloc int[16];
        Span<int> idx2 = stackalloc int[16];
        for (var i = 0; i < 16; i++) idx[i] = Bits(IsAnchor(i) ? ib - 1 : ib);
        if (ib2 > 0) for (var i = 0; i < 16; i++) idx2[i] = Bits(i == 0 ? ib2 - 1 : ib2);
        Span<int> px = stackalloc int[4];
        for (var i = 0; i < 16; i++)
        {
            var s = SubsetOf(i);
            int cIdx = idx[i], cBits = ib, aIdx = idx[i], aBits = ib;
            if (ib2 > 0)
            {
                if (indexSel == 1) { cIdx = idx2[i]; cBits = ib2; }
                else { aIdx = idx2[i]; aBits = ib2; }
            }
            int cw = Weights[cBits][cIdx], aw = Weights[aBits][aIdx];
            for (var c = 0; c < 3; c++) px[c] = (ep[s * 2, c] * (64 - cw) + ep[s * 2 + 1, c] * cw + 32) >> 6;
            px[3] = ab > 0 ? (ep[s * 2, 3] * (64 - aw) + ep[s * 2 + 1, 3] * aw + 32) >> 6 : 255;
            if (rotation > 0) (px[3], px[rotation - 1]) = (px[rotation - 1], px[3]);
            var o = ((by * 4 + (i >> 2)) * w + bx * 4 + (i & 3)) * 4;
            output[o] = (byte)px[0]; output[o + 1] = (byte)px[1]; output[o + 2] = (byte)px[2]; output[o + 3] = (byte)px[3];
        }
    }
}
