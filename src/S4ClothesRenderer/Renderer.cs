using System.Buffers.Binary;
using System.IO.Compression;
using SharpDX;

namespace S4ClothesRenderer;

public static class Renderer
{
    private const int Supersample = 2;
    private static readonly float[,] HairBase = { { 38, 25, 17 }, { 168, 118, 78 } };
    private static readonly float[,] HairHighlight = { { 62, 40, 24 }, { 210, 160, 100 } };

    public static bool IsPlaceholder(List<Mesh> meshes) => meshes.Sum(m => m.Indices.Length / 3) <= 4;

    private static void Sample(Tex tex, float u, float v, Span<float> s)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);
        int w = tex.Width, h = tex.Height;
        float x = u * (w - 1), y = v * (h - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
        float fx = x - x0, fy = y - y0;
        var d = tex.Rgba;
        int i00 = (y0 * w + x0) * 4, i01 = (y0 * w + x1) * 4, i10 = (y1 * w + x0) * 4, i11 = (y1 * w + x1) * 4;
        for (var c = 0; c < 4; c++)
        {
            float a = d[i00 + c], b = d[i01 + c];
            float e = d[i10 + c], f = d[i11 + c];
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

    public static byte[] Render(List<Mesh> meshes, Tex tex, Camera cam, int size, bool hair, RenderBuffers buffers)
    {
        var r = size * Supersample;
        buffers.Ensure(r * r);
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
        var (bx0, by0, bx1, by1) = Raster(meshes, tex, cam, hair, r, cx, cy, scale, buffers);
        var visibleSide = Math.Max(bx1 - bx0 + 1, by1 - by0 + 1);
        if (bx1 >= 0 && visibleSide < r * 0.5f)
        {
            var zx = ((bx0 + bx1 + 1) / 2f - r / 2f) / scale + cx;
            var zy = ((by0 + by1 + 1) / 2f - r / 2f) / scale + cy;
            (bx0, by0, bx1, by1) = Raster(meshes, tex, cam, hair, r, zx, zy, scale * r * 0.9f / visibleSide, buffers);
        }
        if (bx1 < 0) return null;
        var side = Math.Max(16, Math.Max(bx1 - bx0 + 1, by1 - by0 + 1) / 0.9f);
        var ox = (bx0 + bx1 + 1) / 2f - side / 2;
        var oy = (by0 + by1 + 1) / 2f - side / 2;
        return Downsample(buffers.Color, r, ox, oy, side, size, new byte[size * size * 4]);
    }

    public static byte[] RenderFixed(List<Mesh> meshes, Camera cam, int size, float cx, float cy, float span, RenderBuffers buffers)
    {
        var r = size * Supersample;
        buffers.Ensure(r * r);
        Raster(meshes, null, cam, false, r, cx, cy, r / span, buffers);
        return Downsample(buffers.Color, r, 0, 0, r, size, buffers.Frame(size * size * 4));
    }

    private static byte[] Downsample(byte[] color, int r, float ox, float oy, float side, int size, byte[] output)
    {
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
                        float al = color[i + 3];
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
                else
                {
                    output[o] = output[o + 1] = output[o + 2] = 0;
                }
                output[o + 3] = (byte)(ca / (taps * taps));
            }
        }
        return output;
    }

    private const float FrontBias = 1e-4f;
    private static readonly float[] Paint = [228, 230, 234];
    private static readonly float[] Glass = [28, 34, 42];
    private const float GlassAlpha = 0.55f;
    private const float HalfX = 0.167f, HalfY = 0.417f, HalfZ = 0.894f;

    private static (int, int) RowSpan(int x0, int x1, float a0, float c0, float a1, float c1)
    {
        float lo = x0 + 0.5f, hi = x1 + 0.5f;
        Edge(a0, c0, ref lo, ref hi);
        Edge(a1, c1, ref lo, ref hi);
        Edge(-(a0 + a1), 1 - c0 - c1, ref lo, ref hi);
        return (Math.Max(x0, (int)MathF.Floor(lo - 0.5f) - 2), Math.Min(x1, (int)MathF.Ceiling(hi - 0.5f) + 2));
    }

    private static void Edge(float a, float c, ref float lo, ref float hi)
    {
        if (a > 1e-12f) lo = Math.Max(lo, -c / a);
        else if (a < -1e-12f) hi = Math.Min(hi, -c / a);
    }

    private static (int, int, int, int) Raster(List<Mesh> meshes, Tex tex, Camera cam, bool hair, int r, float cx, float cy, float scale, RenderBuffers buffers)
    {
        var color = buffers.Color;
        var depth = buffers.Depth;
        Array.Clear(color, 0, r * r * 4);
        Array.Fill(depth, float.NegativeInfinity, 0, r * r);
        int vx0 = r, vy0 = r, vx1 = -1, vy1 = -1;
        Span<float> s = stackalloc float[4];
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var m in meshes)
            {
                var surface = m.Surface;
                if ((surface == Surface.Glass) != (pass == 1)) continue;
                var mtex = m.Tex ?? tex;
                var positions = m.Positions;
                var n = positions.Length / 3;
                var sp = buffers.Points(n * 3);
                for (var i = 0; i < n; i++)
                {
                    var (sx, sy, sz) = cam.Project(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                    sp[i * 3] = (sx - cx) * scale + r / 2f;
                    sp[i * 3 + 1] = (sy - cy) * scale + r / 2f;
                    sp[i * 3 + 2] = sz;
                }
                var alphaTest = mtex != null && mtex.UseAlpha && m.AlphaTest;
                var hairMesh = mtex != null && (hair || (m.Hair && mtex.HairEncoded));
                var paint = m.PaintColor ?? Paint;
                var uvs = m.Uvs;
                var normals = m.Normals;
                var hasNormals = m.HasNormals;
                var palette = m.PaletteTex;
                var tint = m.Tint;
                var tintU = m.TintU;
                var idx = m.Indices;
                for (var t = 0; t + 2 < idx.Length; t += 3)
                {
                    int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                    if (a >= n || b >= n || c >= n) continue;
                    float ax = sp[a * 3], ay = sp[a * 3 + 1], bx = sp[b * 3], by = sp[b * 3 + 1], qx = sp[c * 3], qy = sp[c * 3 + 1];
                    var area = (bx - ax) * (qy - ay) - (by - ay) * (qx - ax);
                    if (MathF.Abs(area) < 1e-9f) continue;
                    var inv = 1 / area;
                    float za = sp[a * 3 + 2], zb = sp[b * 3 + 2], zc = sp[c * 3 + 2];
                    if (hasNormals && cam.Project(normals[a * 3] + normals[b * 3] + normals[c * 3], normals[a * 3 + 1] + normals[b * 3 + 1] + normals[c * 3 + 1], normals[a * 3 + 2] + normals[b * 3 + 2] + normals[c * 3 + 2]).Z > 0)
                    {
                        za += FrontBias; zb += FrontBias; zc += FrontBias;
                    }
                    var x0 = Math.Max(0, (int)MathF.Floor(Math.Min(ax, Math.Min(bx, qx))));
                    var x1 = Math.Min(r - 1, (int)MathF.Ceiling(Math.Max(ax, Math.Max(bx, qx))));
                    var y0 = Math.Max(0, (int)MathF.Floor(Math.Min(ay, Math.Min(by, qy))));
                    var y1 = Math.Min(r - 1, (int)MathF.Ceiling(Math.Max(ay, Math.Max(by, qy))));
                    for (var y = y0; y <= y1; y++)
                    {
                        var py = y + 0.5f;
                        float ayp = (ay - py) * inv, byp = (by - py) * inv, qyp = (qy - py) * inv;
                        var (xs, xe) = RowSpan(x0, x1, byp - qyp, bx * qyp - byp * qx, qyp - ayp, qx * ayp - qyp * ax);
                        for (var x = xs; x <= xe; x++)
                        {
                            var px = x + 0.5f;
                            var w0 = (bx - px) * qyp - byp * (qx - px);
                            if (w0 < 0) continue;
                            var w1 = (qx - px) * ayp - qyp * (ax - px);
                            var w2 = 1 - w0 - w1;
                            if (w1 < 0 || w2 < 0) continue;
                            var z = w0 * za + w1 * zb + w2 * zc;
                            var pi = y * r + x;
                            if (z <= depth[pi]) continue;
                            if (surface == Surface.Paint)
                            {
                                s[0] = paint[0]; s[1] = paint[1]; s[2] = paint[2];
                            }
                            else if (surface == Surface.Glass)
                            {
                                s[0] = Glass[0]; s[1] = Glass[1]; s[2] = Glass[2];
                            }
                            else if (mtex != null)
                            {
                                var u = w0 * uvs[a * 2] + w1 * uvs[b * 2] + w2 * uvs[c * 2];
                                var v = w0 * uvs[a * 2 + 1] + w1 * uvs[b * 2 + 1] + w2 * uvs[c * 2 + 1];
                                Sample(mtex, u, v, s);
                                if (palette != null)
                                {
                                    var tu = tint == 2 ? (MathF.Round(s[3]) - 32) / 128 : w0 * tintU[a] + w1 * tintU[b] + w2 * tintU[c];
                                    var o = Math.Clamp((int)(tu * palette.Width), 0, palette.Width - 1) * 4;
                                    for (var k = 0; k < 3; k++) s[k] *= palette.Rgba[o + k] / 255f;
                                    if (tint == 2) s[3] = 255;
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
                            if (hasNormals)
                            {
                                var nx = w0 * normals[a * 3] + w1 * normals[b * 3] + w2 * normals[c * 3];
                                var ny = w0 * normals[a * 3 + 1] + w1 * normals[b * 3 + 1] + w2 * normals[c * 3 + 1];
                                var nz = w0 * normals[a * 3 + 2] + w1 * normals[b * 3 + 2] + w2 * normals[c * 3 + 2];
                                var nl = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                                if (nl == 0) nl = 1;
                                var (vx, vy, vz) = cam.Project(nx / nl, ny / nl, nz / nl);
                                var lambert = MathF.Max(0, -vx * 0.25f - vy * 0.35f + vz * 0.9f);
                                light = vz < -0.05f ? 0.45f : 0.62f + 0.42f * lambert;
                                if (surface != Surface.Textured && vz > -0.05f)
                                {
                                    var facing = Math.Clamp(vz, 0, 1);
                                    fresnel = MathF.Pow(1 - facing, 4);
                                    spec = MathF.Pow(MathF.Max(0, -vx * HalfX - vy * HalfY + vz * HalfZ), 30);
                                    light = 0.38f + 0.72f * MathF.Max(0, -vx * 0.3f - vy * 0.75f + vz * 0.6f);
                                    var reflectY = 2 * facing * vy;
                                    env = 30 + 215 * Math.Clamp(0.5f - reflectY * 2.5f, 0, 1);
                                }
                            }
                            var ci = pi * 4;
                            if (surface == Surface.Glass)
                            {
                                var alpha = Math.Min(0.92f, GlassAlpha + 0.35f * fresnel);
                                var dstA = color[ci + 3] / 255f;
                                var outA = alpha + dstA * (1 - alpha);
                                for (var k = 0; k < 3; k++)
                                {
                                    var glass = s[k] * light + env * (0.18f + 0.5f * fresnel) + spec * 255;
                                    color[ci + k] = (byte)((Math.Min(255, glass) * alpha + color[ci + k] * dstA * (1 - alpha)) / outA);
                                }
                                color[ci + 3] = (byte)(outA * 255);
                            }
                            else
                            {
                                depth[pi] = z;
                                if (surface == Surface.Paint)
                                {
                                    for (var k = 0; k < 3; k++) color[ci + k] = (byte)Math.Min(255, s[k] * light * (1 - 0.35f * fresnel) + env * (0.08f + 0.55f * fresnel) + spec * 205);
                                }
                                else
                                {
                                    for (var k = 0; k < 3; k++) color[ci + k] = (byte)Math.Min(255, s[k] * light);
                                }
                                color[ci + 3] = 255;
                            }
                            if (x < vx0) vx0 = x;
                            if (x > vx1) vx1 = x;
                            if (y < vy0) vy0 = y;
                            if (y > vy1) vy1 = y;
                        }
                    }
                }
            }
        }
        return (vx0, vy0, vx1, vy1);
    }
}

public sealed class RenderBuffers
{
    public byte[] Color = [];
    public float[] Depth = [];
    private float[] points = [];
    private byte[] frame = [];

    public void Ensure(int pixels)
    {
        if (Depth.Length >= pixels) return;
        Color = new byte[pixels * 4];
        Depth = new float[pixels];
    }

    public float[] Points(int count) => points.Length >= count ? points : points = new float[count];

    public byte[] Frame(int length) => frame.Length == length ? frame : frame = new byte[length];
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
        var raw = Filter(rgba, width, height);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) z.Write(raw);
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

    private static byte[] Filter(byte[] rgba, int width, int height)
    {
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        var rows = new byte[5][];
        for (var f = 0; f < 5; f++) rows[f] = new byte[stride];
        var zero = new byte[stride];
        Span<long> sums = stackalloc long[5];
        for (var y = 0; y < height; y++)
        {
            var cur = y * stride;
            var above = y > 0 ? rgba : zero;
            var prev = y > 0 ? cur - stride : 0;
            byte[] none = rows[0], sub = rows[1], up = rows[2], average = rows[3], paeth = rows[4];
            long s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
            for (var i = 0; i < stride; i++)
            {
                int x = rgba[cur + i], b = above[prev + i], a = 0, c = 0;
                if (i >= 4)
                {
                    a = rgba[cur + i - 4];
                    c = above[prev + i - 4];
                }
                var p = a + b - c;
                int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                byte v0 = (byte)x, v1 = (byte)(x - a), v2 = (byte)(x - b), v3 = (byte)(x - ((a + b) >> 1)), v4 = (byte)(x - (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                none[i] = v0; sub[i] = v1; up[i] = v2; average[i] = v3; paeth[i] = v4;
                s0 += Math.Abs((int)(sbyte)v0); s1 += Math.Abs((int)(sbyte)v1); s2 += Math.Abs((int)(sbyte)v2); s3 += Math.Abs((int)(sbyte)v3); s4 += Math.Abs((int)(sbyte)v4);
            }
            sums[0] = s0; sums[1] = s1; sums[2] = s2; sums[3] = s3; sums[4] = s4;
            var best = 0;
            for (var f = 1; f < 5; f++) if (sums[f] < sums[best]) best = f;
            raw[y * (stride + 1)] = (byte)best;
            Buffer.BlockCopy(rows[best], 0, raw, y * (stride + 1) + 1, stride);
        }
        return raw;
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
