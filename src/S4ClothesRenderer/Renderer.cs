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
        int x0 = (int)x, y0 = (int)y, x1 = x0 + 1 < w ? x0 + 1 : x0, y1 = y0 + 1 < h ? y0 + 1 : y0;
        float fx = x - x0, fy = y - y0;
        float w00 = (1 - fx) * (1 - fy), w01 = fx * (1 - fy), w10 = (1 - fx) * fy, w11 = fx * fy;
        var d = tex.Rgba;
        int i00 = (y0 * w + x0) * 4, i01 = (y0 * w + x1) * 4, i10 = (y1 * w + x0) * 4, i11 = (y1 * w + x1) * 4;
        s[0] = d[i00] * w00 + d[i01] * w01 + d[i10] * w10 + d[i11] * w11;
        s[1] = d[i00 + 1] * w00 + d[i01 + 1] * w01 + d[i10 + 1] * w10 + d[i11 + 1] * w11;
        s[2] = d[i00 + 2] * w00 + d[i01 + 2] * w01 + d[i10 + 2] * w10 + d[i11 + 2] * w11;
        s[3] = d[i00 + 3] * w00 + d[i01 + 3] * w01 + d[i10 + 3] * w10 + d[i11 + 3] * w11;
    }

    private static (float[] Points, float[] Normals, int Count) Project(Mesh m, Camera cam, int r, float cx, float cy, float scale, RenderBuffers buffers)
    {
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
        if (!m.HasNormals) return (sp, null, n);
        var normals = m.Normals;
        var np = buffers.Normals(n * 3);
        for (var i = 0; i < n; i++)
        {
            var (nx, ny, nz) = cam.Project(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]);
            np[i * 3] = nx;
            np[i * 3 + 1] = ny;
            np[i * 3 + 2] = nz;
        }
        return (sp, np, n);
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
        var xs = new int[size * taps];
        var ys = new int[size * taps];
        for (var i = 0; i < size * taps; i++)
        {
            var sx = (int)MathF.Floor(ox + (i / taps + (i % taps + 0.5f) / taps) * step);
            var sy = (int)MathF.Floor(oy + (i / taps + (i % taps + 0.5f) / taps) * step);
            xs[i] = sx < 0 || sx >= r ? -1 : sx;
            ys[i] = sy < 0 || sy >= r ? -1 : sy;
        }
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                float cr = 0, cg = 0, cb = 0, ca = 0;
                for (var ty = 0; ty < taps; ty++)
                {
                    var sy = ys[y * taps + ty];
                    if (sy < 0) continue;
                    var row = sy * r;
                    for (var tx = 0; tx < taps; tx++)
                    {
                        var sx = xs[x * taps + tx];
                        if (sx < 0) continue;
                        var i = (row + sx) * 4;
                        float al = color[i + 3];
                        if (al == 0) continue;
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
    private const float KeyX = -0.45f, KeyY = -0.6f, KeyZ = 0.66f;
    private const float HalfX = -0.247f, HalfY = -0.329f, HalfZ = 0.911f;
    private const int AoTaps = 12;
    private static readonly float[] Linear = [.. Enumerable.Range(0, 256).Select(i => MathF.Pow(i / 255f, 2.2f))];
    private static readonly byte[] Display = [.. Enumerable.Range(0, 4096).Select(i => (byte)MathF.Round(255 * MathF.Pow(Filmic(i * i / 4194304f), 1 / 2.2f)))];

    private static float Filmic(float x) => x < 0.6f ? x : 0.6f + 0.4f * (1 - MathF.Exp((0.6f - x) / 0.4f));

    private static byte Tone(float x) => Display[Math.Min(4095, (int)(MathF.Sqrt(x) * 2048))];

    private static float Glossy(float nh, float power) => nh / (power - power * nh + nh);

    private static void Occlude(byte[] color, float[] depth, int r, float scale, int x0, int y0, int x1, int y1)
    {
        var radius = r / 40f;
        var range = radius / scale;
        Span<int> dx = stackalloc int[AoTaps];
        Span<int> dy = stackalloc int[AoTaps];
        Span<float> slope = stackalloc float[AoTaps];
        for (var i = 0; i < AoTaps; i++)
        {
            var ring = i / 4 + 1;
            var angle = (i % 4 + (ring - 1) / 3f) * MathF.PI / 4;
            dx[i] = (int)MathF.Round(MathF.Cos(angle) * radius * ring / 3);
            dy[i] = (int)MathF.Round(MathF.Sin(angle) * radius * ring / 3);
            slope[i] = scale / MathF.Max(1, MathF.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]));
        }
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var pi = y * r + x;
                var z = depth[pi];
                if (float.IsNegativeInfinity(z)) continue;
                float occ = 0;
                for (var i = 0; i < AoTaps; i++)
                {
                    int ax = x + dx[i], ay = y + dy[i], bx = x - dx[i], by = y - dy[i];
                    var ha = (uint)ax < (uint)r && (uint)ay < (uint)r ? depth[ay * r + ax] - z : float.NegativeInfinity;
                    var hb = (uint)bx < (uint)r && (uint)by < (uint)r ? depth[by * r + bx] - z : float.NegativeInfinity;
                    var o = MathF.Min(ha * slope[i], 1) + MathF.Min(hb * slope[i], 1);
                    if (o > 0) occ += o * Math.Clamp(2 - MathF.Max(ha, hb) / range, 0, 1);
                }
                var keep = 1 - 0.8f * occ / (2 * AoTaps);
                var ci = pi * 4;
                for (var k = 0; k < 3; k++) color[ci + k] = (byte)(color[ci + k] * keep);
            }
        }
    }

    private static (int, int) RowSpan(int x0, int x1, float a0, float c0, float a1, float c1)
    {
        float lo = x0 + 0.5f, hi = x1 + 0.5f;
        Edge(a0, c0, ref lo, ref hi);
        Edge(a1, c1, ref lo, ref hi);
        Edge(-(a0 + a1), 1 - c0 - c1, ref lo, ref hi);
        return (Math.Max(x0, (int)MathF.Floor(lo - 0.5f)), Math.Min(x1, (int)MathF.Ceiling(hi - 0.5f)));
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
        var mask = meshes.Any(m => m.Decal != null && m.Uv1 != null) ? buffers.Mask(r * r) : null;
        if (mask != null) Array.Clear(mask, 0, r * r);
        int vx0 = r, vy0 = r, vx1 = -1, vy1 = -1;
        Span<float> s = stackalloc float[4];
        Span<float> d = stackalloc float[4];
        foreach (var m in meshes)
        {
            var mtex = m.Tex ?? tex;
            if (m.Surface == Surface.Glass || (mtex != null && mtex.UseAlpha && m.AlphaTest)) continue;
            var (sp, np, n) = Project(m, cam, r, cx, cy, scale, buffers);
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
                if (np != null && np[a * 3 + 2] + np[b * 3 + 2] + np[c * 3 + 2] > 0)
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
                    float a0 = byp - qyp, c0 = bx * qyp - byp * qx, a1 = qyp - ayp, c1 = qx * ayp - qyp * ax;
                    var (xs, xe) = RowSpan(x0, x1, a0, c0, a1, c1);
                    float w0 = c0 + a0 * (xs + 0.5f), w1 = c1 + a1 * (xs + 0.5f);
                    for (var x = xs; x <= xe; x++, w0 += a0, w1 += a1)
                    {
                        var w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        var z = w0 * za + w1 * zb + w2 * zc;
                        var pi = y * r + x;
                        if (z > depth[pi]) depth[pi] = z;
                    }
                }
            }
        }
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var m in meshes)
            {
                var surface = m.Surface;
                if ((surface == Surface.Glass) != (pass == 1)) continue;
                var mtex = m.Tex ?? tex;
                var (sp, np, n) = Project(m, cam, r, cx, cy, scale, buffers);
                var alphaTest = mtex != null && mtex.UseAlpha && m.AlphaTest;
                var hairMesh = mtex != null && (hair || (m.Hair && mtex.HairEncoded));
                var paint = m.PaintColor ?? Paint;
                var uvs = m.Uvs;
                var hasNormals = np != null;
                var palette = m.PaletteTex;
                var tint = m.Tint;
                var tintU = m.TintU;
                var idx = m.Indices;
                var uv1 = m.Uv1;
                var decal = uv1 == null ? null : m.Decal;
                var zone = m.DecalZone;
                var livery = m.LiveryTex;
                var liveryUvs = uv1 ?? uvs;
                var bump = hasNormals ? m.BumpTex : null;
                var specMap = m.SpecTex;
                var bumpiness = m.Bumpiness;
                var specIntensity = m.SpecIntensity;
                var specPower = m.SpecPower;
                for (var t = 0; t + 2 < idx.Length; t += 3)
                {
                    int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                    if (a >= n || b >= n || c >= n) continue;
                    float ax = sp[a * 3], ay = sp[a * 3 + 1], bx = sp[b * 3], by = sp[b * 3 + 1], qx = sp[c * 3], qy = sp[c * 3 + 1];
                    var area = (bx - ax) * (qy - ay) - (by - ay) * (qx - ax);
                    if (MathF.Abs(area) < 1e-9f) continue;
                    var inv = 1 / area;
                    float za = sp[a * 3 + 2], zb = sp[b * 3 + 2], zc = sp[c * 3 + 2];
                    var front = !hasNormals || np[a * 3 + 2] + np[b * 3 + 2] + np[c * 3 + 2] > 0;
                    if (hasNormals && front)
                    {
                        za += FrontBias; zb += FrontBias; zc += FrontBias;
                    }
                    float tanX = 0, tanY = 0, tanZ = 0, bitX = 0, bitY = 0, bitZ = 0;
                    var bumped = bump != null && front;
                    if (bumped)
                    {
                        float du1 = uvs[b * 2] - uvs[a * 2], dv1 = uvs[b * 2 + 1] - uvs[a * 2 + 1], du2 = uvs[c * 2] - uvs[a * 2], dv2 = uvs[c * 2 + 1] - uvs[a * 2 + 1];
                        float ex = bx - ax, ey = by - ay, ez = (zb - za) * scale, fx = qx - ax, fy = qy - ay, fz = (zc - za) * scale;
                        tanX = ex * dv2 - fx * dv1; tanY = ey * dv2 - fy * dv1; tanZ = ez * dv2 - fz * dv1;
                        bitX = fx * du1 - ex * du2; bitY = fy * du1 - ey * du2; bitZ = fz * du1 - ez * du2;
                        var det = du1 * dv2 - du2 * dv1;
                        var tl = tanX * tanX + tanY * tanY + tanZ * tanZ;
                        var bl = bitX * bitX + bitY * bitY + bitZ * bitZ;
                        bumped = MathF.Abs(det) > 1e-12f && tl > 1e-12f && bl > 1e-12f;
                        var ts = (det > 0 ? 1 : -1) / MathF.Sqrt(tl);
                        var bs = (det > 0 ? 1 : -1) / MathF.Sqrt(bl);
                        tanX *= ts; tanY *= ts; tanZ *= ts;
                        bitX *= bs; bitY *= bs; bitZ *= bs;
                    }
                    var x0 = Math.Max(0, (int)MathF.Floor(Math.Min(ax, Math.Min(bx, qx))));
                    var x1 = Math.Min(r - 1, (int)MathF.Ceiling(Math.Max(ax, Math.Max(bx, qx))));
                    var y0 = Math.Max(0, (int)MathF.Floor(Math.Min(ay, Math.Min(by, qy))));
                    var y1 = Math.Min(r - 1, (int)MathF.Ceiling(Math.Max(ay, Math.Max(by, qy))));
                    for (var y = y0; y <= y1; y++)
                    {
                        var py = y + 0.5f;
                        float ayp = (ay - py) * inv, byp = (by - py) * inv, qyp = (qy - py) * inv;
                        float a0 = byp - qyp, c0 = bx * qyp - byp * qx, a1 = qyp - ayp, c1 = qx * ayp - qyp * ax;
                        var (xs, xe) = RowSpan(x0, x1, a0, c0, a1, c1);
                        float w0 = c0 + a0 * (xs + 0.5f), w1 = c1 + a1 * (xs + 0.5f);
                        for (var x = xs; x <= xe; x++, w0 += a0, w1 += a1)
                        {
                            var w2 = 1 - w0 - w1;
                            if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                            var z = w0 * za + w1 * zb + w2 * zc;
                            var pi = y * r + x;
                            if (z < depth[pi]) continue;
                            var hit = false;
                            var u = w0 * uvs[a * 2] + w1 * uvs[b * 2] + w2 * uvs[c * 2];
                            var v = w0 * uvs[a * 2 + 1] + w1 * uvs[b * 2 + 1] + w2 * uvs[c * 2 + 1];
                            if (surface == Surface.Paint)
                            {
                                s[0] = paint[0]; s[1] = paint[1]; s[2] = paint[2];
                                if (livery != null)
                                {
                                    Sample(livery, w0 * liveryUvs[a * 2] + w1 * liveryUvs[b * 2] + w2 * liveryUvs[c * 2], w0 * liveryUvs[a * 2 + 1] + w1 * liveryUvs[b * 2 + 1] + w2 * liveryUvs[c * 2 + 1], d);
                                    var la = d[3] / 255;
                                    for (var k = 0; k < 3; k++) s[k] += (d[k] - s[k]) * la;
                                }
                            }
                            else if (surface == Surface.Glass)
                            {
                                s[0] = Glass[0]; s[1] = Glass[1]; s[2] = Glass[2];
                            }
                            else if (mtex != null)
                            {
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
                                if (decal != null)
                                {
                                    var du = w0 * uv1[a * 2] + w1 * uv1[b * 2] + w2 * uv1[c * 2];
                                    var dv = w0 * uv1[a * 2 + 1] + w1 * uv1[b * 2 + 1] + w2 * uv1[c * 2 + 1];
                                    var tile = (int)MathF.Floor(dv);
                                    if (du >= 1 && du < 2 && tile == -(zone * 2 + 1))
                                    {
                                        Sample(decal, du - 1, dv - tile, d);
                                        var da = d[3] / 255;
                                        if (da > 0.02f)
                                        {
                                            for (var k = 0; k < 3; k++) s[k] = s[k] * (1 - da) + d[k] * da;
                                            hit = true;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                s[0] = s[1] = s[2] = tex == null ? 150 : 200;
                            }
                            float nx = 0, ny = 0, nz = 1;
                            if (hasNormals)
                            {
                                nx = w0 * np[a * 3] + w1 * np[b * 3] + w2 * np[c * 3];
                                ny = w0 * np[a * 3 + 1] + w1 * np[b * 3 + 1] + w2 * np[c * 3 + 1];
                                nz = w0 * np[a * 3 + 2] + w1 * np[b * 3 + 2] + w2 * np[c * 3 + 2];
                                var nl = MathF.ReciprocalSqrtEstimate(Math.Max(1e-12f, nx * nx + ny * ny + nz * nz));
                                if (!front) nl = -nl;
                                nx *= nl; ny *= nl; nz *= nl;
                            }
                            if (bumped)
                            {
                                Sample(bump, u, v, d);
                                float hx = d[0] / 127.5f - 1, hy = d[1] / 127.5f - 1;
                                var hz = MathF.Sqrt(MathF.Max(0, 1 - hx * hx - hy * hy));
                                hx *= bumpiness; hy *= bumpiness;
                                nx = tanX * hx + bitX * hy + nx * hz;
                                ny = tanY * hx + bitY * hy + ny * hz;
                                nz = tanZ * hx + bitZ * hy + nz * hz;
                                var bl = MathF.ReciprocalSqrtEstimate(Math.Max(1e-12f, nx * nx + ny * ny + nz * nz));
                                nx *= bl; ny *= bl; nz *= bl;
                            }
                            var edge = 1 - Math.Clamp(nz, 0, 1);
                            var key = MathF.Max(0, nx * KeyX + ny * KeyY + nz * KeyZ + 0.25f) / 1.25f;
                            var fill = MathF.Max(0, nx * 0.7f - ny * 0.15f + nz * 0.7f + 0.25f) / 1.25f;
                            var rim = MathF.Max(0, nx * 0.6f - ny * 0.8f) * edge * edge;
                            var light = (key * 1.1f + fill * 0.25f + rim * 0.4f + 0.15f - ny * 0.07f) * (front ? 1 : 0.25f);
                            var nh = MathF.Max(0, nx * HalfX + ny * HalfY + nz * HalfZ);
                            float spec = 0, env = 0;
                            if (specMap != null)
                            {
                                Sample(specMap, u, v, d);
                                var power = MathF.Max(2, specPower * d[3] / 255);
                                spec = 0.35f * specIntensity * d[0] / 255 * Glossy(nh, power);
                            }
                            if (surface != Surface.Textured)
                            {
                                var up = -2 * nz * ny;
                                env = 0.03f + Math.Clamp((up + 0.03f) * 10, 0, 1) * (0.5f + MathF.Max(0, up));
                                spec += Glossy(nh, 900) * 3;
                            }
                            var ci = pi * 4;
                            if (surface == Surface.Glass)
                            {
                                var fresnel = edge * edge * edge * edge;
                                var alpha = Math.Min(0.92f, GlassAlpha + 0.35f * fresnel);
                                var dstA = color[ci + 3] / 255f;
                                var outA = alpha + dstA * (1 - alpha);
                                var reflect = env * (0.1f + 0.9f * fresnel * edge) + spec;
                                for (var k = 0; k < 3; k++)
                                {
                                    var glass = Tone(Linear[(int)(s[k] + 0.5f)] * light + reflect);
                                    color[ci + k] = (byte)((glass * alpha + color[ci + k] * dstA * (1 - alpha)) / outA);
                                }
                                color[ci + 3] = (byte)(outA * 255);
                            }
                            else
                            {
                                depth[pi] = z;
                                if (mask != null) mask[pi] = hit ? (byte)1 : (byte)0;
                                if (surface == Surface.Paint)
                                {
                                    var clear = edge * edge * edge * edge * edge;
                                    var diffuse = (light + Glossy(nh, 24) * 0.6f + env * 0.35f) * (1 - clear);
                                    var reflect = clear * env + spec;
                                    for (var k = 0; k < 3; k++) color[ci + k] = Tone(Linear[(int)(s[k] + 0.5f)] * diffuse + reflect);
                                }
                                else
                                {
                                    for (var k = 0; k < 3; k++) color[ci + k] = Tone(Linear[(int)(s[k] + 0.5f)] * light + spec);
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
        Occlude(color, depth, r, scale, vx0, vy0, vx1, vy1);
        buffers.DecalPixels = 0;
        if (mask != null)
        {
            for (var i = 0; i < r * r; i++) buffers.DecalPixels += mask[i];
        }
        return (vx0, vy0, vx1, vy1);
    }
}

public sealed class RenderBuffers
{
    public byte[] Color = [];
    public float[] Depth = [];
    public int DecalPixels;
    private float[] points = [];
    private byte[] frame = [];
    private byte[] mask = [];
    private float[] normals = [];

    public void Ensure(int pixels)
    {
        if (Depth.Length >= pixels) return;
        Color = new byte[pixels * 4];
        Depth = new float[pixels];
    }

    public float[] Points(int count) => points.Length >= count ? points : points = new float[count];

    public byte[] Mask(int count) => mask.Length >= count ? mask : mask = new byte[count];

    public float[] Normals(int count) => normals.Length >= count ? normals : normals = new float[count];

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
