using System.Buffers.Binary;
using System.IO.Compression;
using SharpDX;

namespace S4ClothesRenderer;

public static class Renderer
{
    private const int Supersample = 2;
    private static readonly float[,] HairBase = { { 38, 25, 17 }, { 168, 118, 78 } };
    private static readonly float[,] HairHighlight = { { 62, 40, 24 }, { 210, 160, 100 } };

    public static bool IsPlaceholder(List<Mesh> meshes, Tex tex) =>
        tex != null && Math.Max(tex.Width, tex.Height) <= 64 &&meshes.Sum(m => m.Indices.Length / 3) <= 4;

    private static void Sample(Tex tex, float u, float v, Span<float> s)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);
        float x = u * (tex.Width - 1), y = v * (tex.Height - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(tex.Width - 1, x0 + 1), y1 = Math.Min(tex.Height - 1, y0 + 1);
        float fx = x - x0, fy = y - y0;
        var d = tex.Rgba;
        for (var c = 0; c < 4; c++)
        {
            float a = d[(y0 * tex.Width + x0) * 4 + c], b = d[(y0 * tex.Width + x1) * 4 + c];
            float e = d[(y1 * tex.Width + x0) * 4 + c], f = d[(y1 * tex.Width + x1) * 4 + c];
            s[c] = (a * (1 - fx) + b * fx) * (1 - fy) + (e * (1 - fx) + f * fx) * fy;
        }
    }

    private static void HairColor(Span<float> s)
    {
        var t = s[1] / 255;
        var h = Math.Clamp((s[0] - s[2]) / Math.Max(s[1] - s[2], 1), 0, 1);
        for (var c = 0; c < 3; c++)
        {
            var dark = HairBase[0, c] + (HairBase[1, c] - HairBase[0, c]) * t;
            var high = HairHighlight[0, c] + (HairHighlight[1, c] - HairHighlight[0, c]) * t;
            s[c] = dark + (high - dark) * h;
        }
    }

    public static byte[] Render(List<Mesh> meshes, Tex tex, Camera cam, int size, bool hair)
    {
        if (IsPlaceholder(meshes, tex)) return new byte[size * size * 4];
        var r = size * Supersample;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var m in meshes)
        {
            for (var i = 0; i < m.Positions.Length; i += 3)
            {
                var (sx, sy, _) = cam.Project(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]);
                minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx); minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
            }
        }
        var span = Math.Max(maxX - minX, maxY - minY);
        if (!(span > 0)) span = 1;
        var scale = r * 0.9f / span;
        float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        var color = new float[r * r * 4];
        var depth = new float[r * r];
        Raster(meshes, tex, cam, hair, r, cx, cy, scale, color, depth);
        var (bx0, by0, bx1, by1) = VisibleBox(color, r);
        var visibleSide = Math.Max(bx1 - bx0 + 1, by1 - by0 + 1);
        if (bx1 >= 0 && visibleSide < r * 0.5f)
        {
            var zx = ((bx0 + bx1 + 1) / 2f - r / 2f) / scale + cx;
            var zy = ((by0 + by1 + 1) / 2f - r / 2f) / scale + cy;
            Raster(meshes, tex, cam, hair, r, zx, zy, scale * r * 0.9f / visibleSide, color, depth);
            (bx0, by0, bx1, by1) = VisibleBox(color, r);
        }
        float side = r, ox = 0, oy = 0;
        if (bx1 >= 0)
        {
            side = Math.Max(16, Math.Max(bx1 - bx0 + 1, by1 - by0 + 1) / 0.9f);
            ox = (bx0 + bx1 + 1) / 2f - side / 2;
            oy = (by0 + by1 + 1) / 2f - side / 2;
        }
        var output = new byte[size * size * 4];
        var step = side / size;
        var taps = Math.Max(1, Math.Min(4, (int)MathF.Round(step)));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                float cr = 0, cg = 0, cb = 0, ca = 0;
                for (var ty = 0; ty < taps; ty++)
                {
                    for (var tx = 0; tx < taps; tx++)
                    {
                        var sx = (int)MathF.Floor(ox + (x + (tx + 0.5f) / taps) * step);
                        var sy = (int)MathF.Floor(oy + (y + (ty + 0.5f) / taps) * step);
                        if (sx < 0 || sy < 0 || sx >= r || sy >= r) continue;
                        var i = (sy * r + sx) * 4;
                        var al = color[i + 3];
                        cr += color[i] * al; cg += color[i + 1] * al; cb += color[i + 2] * al; ca += al;
                    }
                }
                var o = (y * size + x) * 4;
                if (ca > 0)
                {
                    output[o] = (byte)Math.Min(255, cr / ca);
                    output[o + 1] = (byte)Math.Min(255, cg / ca);
                    output[o + 2] = (byte)Math.Min(255, cb / ca);
                }
                output[o + 3] = (byte)(ca / (taps * taps));
            }
        }
        return output;
    }

    private static (int, int, int, int) VisibleBox(float[] color, int r)
    {
        int x0 = r, y0 = r, x1 = -1, y1 = -1;
        for (var y = 0; y < r; y++)
        {
            for (var x = 0; x < r; x++)
            {
                if (color[(y * r + x) * 4 + 3] <= 0) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
        }
        return (x0, y0, x1, y1);
    }

    private static readonly float[] Paint = [228, 230, 234];
    private static readonly float[] Glass = [28, 34, 42];
    private const float GlassAlpha = 0.55f;
    private const float HalfX = 0.167f, HalfY = 0.417f, HalfZ = 0.894f;

    private static void Raster(List<Mesh> meshes, Tex tex, Camera cam, bool hair, int r, float cx, float cy, float scale, float[] color, float[] depth)
    {
        Array.Clear(color);
        Array.Fill(depth, float.NegativeInfinity);
        Span<float> s = stackalloc float[4];
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var m in meshes)
            {
                if ((m.Surface == Surface.Glass) != (pass == 1)) continue;
                var mtex = m.Tex ?? tex;
                var n = m.Positions.Length / 3;
                var sp = new float[n * 3];
                for (var i = 0; i < n; i++)
                {
                    var (sx, sy, sz) = cam.Project(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                    sp[i * 3] = (sx - cx) * scale + r / 2f;
                    sp[i * 3 + 1] = (sy - cy) * scale + r / 2f;
                    sp[i * 3 + 2] = sz;
                }
                var alphaTest = mtex != null && mtex.UseAlpha && m.AlphaTest;
                var hairMesh = mtex != null && (hair || (m.Hair && mtex.HairEncoded));
                var idx = m.Indices;
                for (var t = 0; t + 2 < idx.Length; t += 3)
                {
                    int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                    if (a >= n || b >= n || c >= n) continue;
                    float ax = sp[a * 3], ay = sp[a * 3 + 1], bx = sp[b * 3], by = sp[b * 3 + 1], qx = sp[c * 3], qy = sp[c * 3 + 1];
                    var area = (bx - ax) * (qy - ay) - (by - ay) * (qx - ax);
                    if (MathF.Abs(area) < 1e-9f) continue;
                    var x0 = Math.Max(0, (int)MathF.Floor(Math.Min(ax, Math.Min(bx, qx))));
                    var x1 = Math.Min(r - 1, (int)MathF.Ceiling(Math.Max(ax, Math.Max(bx, qx))));
                    var y0 = Math.Max(0, (int)MathF.Floor(Math.Min(ay, Math.Min(by, qy))));
                    var y1 = Math.Min(r - 1, (int)MathF.Ceiling(Math.Max(ay, Math.Max(by, qy))));
                    for (var y = y0; y <= y1; y++)
                    {
                        for (var x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f, py = y + 0.5f;
                            var w0 = ((bx - px) * (qy - py) - (by - py) * (qx - px)) / area;
                            var w1 = ((qx - px) * (ay - py) - (qy - py) * (ax - px)) / area;
                            var w2 = 1 - w0 - w1;
                            if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                            var z = w0 * sp[a * 3 + 2] + w1 * sp[b * 3 + 2] + w2 * sp[c * 3 + 2];
                            var pi = y * r + x;
                            if (z <= depth[pi]) continue;
                            if (m.Surface == Surface.Paint)
                            {
                                var paint = m.PaintColor ?? Paint;
                                s[0] = paint[0]; s[1] = paint[1]; s[2] = paint[2];
                            }
                            else if (m.Surface == Surface.Glass)
                            {
                                s[0] = Glass[0]; s[1] = Glass[1]; s[2] = Glass[2];
                            }
                            else if (mtex != null)
                            {
                                var u = w0 * m.Uvs[a * 2] + w1 * m.Uvs[b * 2] + w2 * m.Uvs[c * 2];
                                var v = w0 * m.Uvs[a * 2 + 1] + w1 * m.Uvs[b * 2 + 1] + w2 * m.Uvs[c * 2 + 1];
                                Sample(mtex, u, v, s);
                                if (m.PaletteTex != null)
                                {
                                    var tu = m.Tint == 2 ? (MathF.Round(s[3]) - 32) / 128 : w0 * m.TintU[a] + w1 * m.TintU[b] + w2 * m.TintU[c];
                                    var p = m.PaletteTex;
                                    var o = Math.Clamp((int)(tu * p.Width), 0, p.Width - 1) * 4;
                                    for (var k = 0; k < 3; k++) s[k] *= p.Rgba[o + k] / 255f;
                                    if (m.Tint == 2) s[3] = 255;
                                }
                                if (alphaTest && s[3] < 90) continue;
                                if (hairMesh) HairColor(s);
                            }
                            else
                            {
                                s[0] = s[1] = s[2] = tex == null ? 150 : 200;
                            }
                            var light = 1f;
                            float fresnel = 0, spec = 0, env = 0;
                            if (m.HasNormals)
                            {
                                var nx = w0 * m.Normals[a * 3] + w1 * m.Normals[b * 3] + w2 * m.Normals[c * 3];
                                var ny = w0 * m.Normals[a * 3 + 1] + w1 * m.Normals[b * 3 + 1] + w2 * m.Normals[c * 3 + 1];
                                var nz = w0 * m.Normals[a * 3 + 2] + w1 * m.Normals[b * 3 + 2] + w2 * m.Normals[c * 3 + 2];
                                var nl = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                                if (nl == 0) nl = 1;
                                var (vx, vy, vz) = cam.Project(nx / nl, ny / nl, nz / nl);
                                var lambert = MathF.Max(0, -vx * 0.25f - vy * 0.35f + vz * 0.9f);
                                light = vz < -0.05f ? 0.45f : 0.62f + 0.42f * lambert;
                                if (m.Surface != Surface.Textured && vz > -0.05f)
                                {
                                    var facing = Math.Clamp(vz, 0, 1);
                                    fresnel = MathF.Pow(1 - facing, 4);
                                    spec = MathF.Pow(MathF.Max(0, -vx * HalfX - vy * HalfY + vz * HalfZ), 30);
                                    light = 0.38f + 0.72f * MathF.Max(0, -vx * 0.3f - vy * 0.75f + vz * 0.6f);
                                    var reflectY = 2 * facing * vy;
                                    env = 30 + 215 * Math.Clamp(0.5f - reflectY * 2.5f, 0, 1);
                                }
                            }
                            if (m.Surface == Surface.Glass)
                            {
                                var alpha = Math.Min(0.92f, GlassAlpha + 0.35f * fresnel);
                                var dstA = color[pi * 4 + 3] / 255f;
                                var outA = alpha + dstA * (1 - alpha);
                                for (var k = 0; k < 3; k++)
                                {
                                    var glass = s[k] * light + env * (0.18f + 0.5f * fresnel) + spec * 255;
                                    color[pi * 4 + k] = (Math.Min(255, glass) * alpha + color[pi * 4 + k] * dstA * (1 - alpha)) / outA;
                                }
                                color[pi * 4 + 3] = outA * 255;
                                continue;
                            }
                            if (m.Surface == Surface.Paint)
                            {
                                depth[pi] = z;
                                for (var k = 0; k < 3; k++) color[pi * 4 + k] = Math.Min(255, s[k] * light * (1 - 0.35f * fresnel) + env * (0.08f + 0.55f * fresnel) + spec * 205);
                                color[pi * 4 + 3] = 255;
                                continue;
                            }
                            depth[pi] = z;
                            color[pi * 4] = s[0] * light;
                            color[pi * 4 + 1] = s[1] * light;
                            color[pi * 4 + 2] = s[2] * light;
                            color[pi * 4 + 3] = 255;
                        }
                    }
                }
            }
        }
    }
}

public readonly struct Camera
{
    private readonly Vector3 right;
    private readonly Vector3 up;
    private readonly Vector3 toward;

    private Camera(Vector3 right, Vector3 up, Vector3 toward)
    {
        this.right = right;
        this.up = up;
        this.toward = toward;
    }

    public (float X, float Y, float Z) Project(float x, float y, float z) =>
        (right.X * x + right.Y * y + right.Z * z, -(up.X * x + up.Y * y + up.Z * z), toward.X * x + toward.Y * y + toward.Z * z);

    public static Camera Toward(float x, float y, float z)
    {
        var c = Vector3.Normalize(new Vector3(x, y, z));
        var r = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, c));
        return new Camera(r, Vector3.Cross(c, r), c);
    }

    public static Camera Of(View view) => view switch
    {
        View.Back => new Camera(-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
        View.Head => new Camera(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
        View.Wrist => new Camera(Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ),
        _ => new Camera(Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitY),
    };
}

public static class Png
{
    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++) Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) z.Write(raw);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        Chunk(output, "IHDR", ihdr);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        output.Write(buffer);
        var typed = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) typed[i] = (byte)type[i];
        data.CopyTo(typed, 4);
        output.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc(typed));
        output.Write(buffer);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(byte[] data)
    {
        var c = 0xffffffff;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xff] ^ (c >> 8);
        return c ^ 0xffffffff;
    }
}
