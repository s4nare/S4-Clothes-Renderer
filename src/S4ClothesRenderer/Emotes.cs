using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using CodeWalker.GameFiles;

namespace S4ClothesRenderer;

public static class Emotes
{
    private const int Fps = 30;
    private const int MaxTexture = 1024;
    private const int PaletteSamples = 12;
    private const string FfmpegZip = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string ActionsMale = "anim@mp_player_intcelebrationmale@";
    private const string ActionsFemale = "anim@mp_player_intcelebrationfemale@";
    private static readonly string[] DancePrefixes = ["anim@amb@nightclub@mini@dance@", "anim@amb@nightclub@dancers@"];
    private static readonly (string Comp, int Male, int Female)[] Outfit = [("head", 0, 0), ("hair", 10, 4), ("uppr", 0, 0), ("lowr", 0, 0), ("feet", 0, 0), ("jbib", 0, 0)];
    public static readonly string[] Formats = ["gif", "webp", "mov", "mp4"];
    private static readonly ConcurrentDictionary<string, (Skeleton Skeleton, List<Mesh> Meshes)> Peds = new();

    public static List<ObjectItem> Catalog(Library lib, List<string> folders)
    {
        var items = new List<ObjectItem>();
        var names = lib.Names(".ycd").Select(n => n[..^4]).ToList();
        foreach (var dict in names)
        {
            var sex = dict.StartsWith(ActionsMale, StringComparison.OrdinalIgnoreCase) ? "male" : dict.StartsWith(ActionsFemale, StringComparison.OrdinalIgnoreCase) ? "female" : null;
            if (sex == null) continue;
            var action = dict[(dict.LastIndexOf('@') + 1)..];
            items.Add(new ObjectItem("emotes", "actions_" + sex, action, dict, action, null, null, sex));
        }
        var dances = names
            .Select(d => (Dict: d, Prefix: DancePrefixes.FirstOrDefault(p => d.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
            .Where(d => d.Prefix != null && !d.Dict.Contains("faces") && !d.Dict.Contains("transitions"))
            .AsParallel().AsOrdered()
            .Select(d => (d.Dict, d.Prefix, Clips: Clips(lib.Get(d.Dict + ".ycd"))))
            .ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dict, prefix, clips) in dances)
        {
            foreach (var clip in clips)
            {
                var hat = clip.IndexOf('^');
                if (hat >= 0 && clip[(hat + 1)..] != "1") continue;
                var name = hat >= 0 ? clip[..hat] : clip;
                if (!name.Contains("male")) name = dict[prefix.Length..].Replace('@', '_').Trim('_') + "_" + name;
                if (seen.Add(name)) items.Add(new ObjectItem("emotes", "dances", name, dict, dict, null, clip, SexOf(dict + clip)));
            }
        }
        foreach (var dict in names)
        {
            var file = lib.Get(dict + ".ycd");
            if (file.Entry != null) continue;
            var root = folders.FirstOrDefault(f => file.Path.StartsWith(f, StringComparison.OrdinalIgnoreCase));
            var relative = root == null ? "" : Path.GetRelativePath(root, file.Path);
            var cut = relative.IndexOfAny(['\\', '/']);
            var group = cut > 0 ? relative[..cut] : Path.GetFileName(root?.TrimEnd('\\', '/') ?? "custom");
            var clips = Clips(file);
            foreach (var clip in clips) items.Add(new ObjectItem("emotes", group, clips.Count > 1 ? dict + "_" + clip : dict, dict, dict, null, clip, SexOf(dict + clip)));
        }
        return items;
    }

    private static List<string> Clips(FileRef file)
    {
        try
        {
            return [.. (file.LoadClips().ClipMapEntries ?? []).Select(e => e?.Clip?.ShortName)
                .Where(n => !string.IsNullOrEmpty(n) && !n.EndsWith("_facial") && !n.EndsWith("_fp")).Distinct().Order()];
        }
        catch
        {
            return [];
        }
    }

    private static string SexOf(string text) => text.Contains("female", StringComparison.OrdinalIgnoreCase) ? "female" : "male";

    public static void Capture(GameArchive game, string ffmpeg, Library lib, ObjectItem item, string output, int size, string format)
    {
        if (ffmpeg == null) throw new InvalidOperationException("ffmpeg");
        if (game == null) throw new InvalidDataException("ped_missing");
        var names = lib.Get(item.Model + ".ycd") is { } file ? Clips(file) : [];
        var name = item.Clip ?? names.FirstOrDefault();
        var clip = lib.Get(item.Model + ".ycd")?.LoadClips().ClipMapEntries?.FirstOrDefault(e => string.Equals(e?.Clip?.ShortName, name, StringComparison.OrdinalIgnoreCase))?.Clip;
        (Animation Animation, float Start, float End, float Rate)[] layers = clip switch
        {
            ClipAnimation single => [(single.Animation, single.StartTime, single.EndTime, single.Rate)],
            ClipAnimationList list => [.. (list.Animations?.Data ?? []).Select(a => (a?.Animation, a?.StartTime ?? 0, a?.EndTime ?? 0, a?.Rate ?? 1))],
            _ => [],
        };
        layers = [.. layers.Where(l => l.Animation != null && l.End > l.Start)];
        if (layers.Length == 0) throw new InvalidDataException("clip_missing");
        var female = item.Sex == "female";
        var (skeleton, ped) = Peds.GetOrAdd(game.Folder + "|" + item.Sex, _ => Ped(game, female));
        var length = layers.Max(l => (l.End - l.Start) / (l.Rate > 0 ? l.Rate : 1));
        var count = Math.Max(1, (int)MathF.Round(length * Fps));
        var rigs = Enumerable.Range(0, count).Select(i => Rig.Animated(skeleton, [.. layers.Select(l => (l.Animation, Math.Min(l.End, l.Start + length * i / count * (l.Rate > 0 ? l.Rate : 1))))])).ToArray();
        var cam = Camera.Toward(0.3f, 1, 0.1f);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        var work = ped.Select(m => m.Copy()).ToList();
        foreach (var rig in rigs.Where((_, i) => i % 4 == 0 || i == count - 1))
        {
            Pose(ped, work, rig);
            foreach (var m in work)
            {
                for (var i = 0; i < m.Positions.Length; i += 3)
                {
                    var (x, y, _) = cam.Project(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]);
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }
        }
        float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, side = Math.Max(maxX - minX, maxY - minY) / 0.9f;
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var buffers = new RenderBuffers();
        void Pipe(IEnumerable<string> args, IEnumerable<Rig> frames)
        {
            var start = new ProcessStartInfo(ffmpeg) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            string[] input = ["-y", "-v", "error", "-threads", "2", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{size}x{size}", "-framerate", Fps.ToString(), "-i", "-"];
            foreach (var arg in input.Concat(args)) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
            var error = process.StandardError.ReadToEndAsync();
            using (var stdin = process.StandardInput.BaseStream)
            {
                foreach (var rig in frames)
                {
                    Pose(ped, work, rig);
                    stdin.Write(Renderer.RenderFixed(work, cam, size, cx, cy, side, buffers));
                }
            }
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(error.Result.Trim().Split('\n').LastOrDefault() ?? "ffmpeg");
        }
        if (format != "gif")
        {
            string[] codec = format switch
            {
                "mp4" => ["-filter_complex", $"color=c=black:s={size}x{size}:r={Fps}[bg];[bg][0]overlay=shortest=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-movflags", "+faststart"],
                "mov" => ["-c:v", "prores_ks", "-profile:v", "4444", "-pix_fmt", "yuva444p10le"],
                _ => ["-c:v", "libwebp_anim", "-q:v", "90", "-compression_level", "1", "-pix_fmt", "yuva420p", "-loop", "0"],
            };
            Pipe([.. codec, output], rigs);
            return;
        }
        var palette = Path.Combine(Path.GetTempPath(), $"s4_palette_{Guid.NewGuid():N}.png");
        try
        {
            Pipe(["-vf", "palettegen=reserve_transparent=1", "-update", "1", palette], rigs.Where((_, i) => i % Math.Max(1, count / PaletteSamples) == 0));
            Pipe(["-i", palette, "-filter_complex", "[0][1]paletteuse=alpha_threshold=128", "-loop", "0", output], rigs);
        }
        finally
        {
            File.Delete(palette);
        }
    }

    private static (Skeleton, List<Mesh>) Ped(GameArchive game, bool female)
    {
        var skeleton = game.Index.Get(female ? "mp_f_freemode_01.yft" : "mp_m_freemode_01.yft")?.LoadFragment().Fragment?.Drawable?.Skeleton
            ?? throw new InvalidDataException("ped_missing");
        var meshes = new List<Mesh>();
        var prefix = female ? "mp_f_freemode_01^" : "mp_m_freemode_01^";
        foreach (var (comp, male, woman) in Outfit)
        {
            var number = (female ? woman : male).ToString("D3");
            var model = game.Files.FirstOrDefault(f => f.FullName.StartsWith($"{prefix}{comp}_{number}_", StringComparison.Ordinal) && f.FullName.EndsWith(".ydd", StringComparison.Ordinal)).File;
            if (model == null) continue;
            var texture = game.Files.FirstOrDefault(f => f.FullName.StartsWith($"{prefix}{comp}_diff_{number}_a", StringComparison.Ordinal)).File;
            var tex = texture == null ? null : Assets3D.ReadTexture(texture.LoadTextures(), MaxTexture);
            var parts = Assets3D.ReadMeshes(model.LoadDrawables());
            foreach (var m in parts) m.Tex = tex;
            meshes.AddRange(parts);
        }
        if (meshes.Count == 0) throw new InvalidDataException("ped_missing");
        return (skeleton, meshes);
    }

    private static void Pose(List<Mesh> bind, List<Mesh> work, Rig rig)
    {
        for (var i = 0; i < bind.Count; i++)
        {
            bind[i].Positions.CopyTo(work[i].Positions, 0);
            bind[i].Normals.CopyTo(work[i].Normals, 0);
        }
        rig.Apply(work);
    }

    public static string Ffmpeg(Action<int, int> report)
    {
        var found = new[] { AppContext.BaseDirectory, Settings.Folder }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(d => Path.Combine(d, "ffmpeg.exe"))
            .FirstOrDefault(File.Exists);
        if (found != null) return found;
        var target = Path.Combine(Settings.Folder, "ffmpeg.exe");
        var zipFile = Path.Combine(Path.GetTempPath(), "s4_ffmpeg.zip");
        using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
        using (var response = http.GetAsync(FfmpegZip, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult().EnsureSuccessStatusCode())
        using (var source = response.Content.ReadAsStream())
        using (var output = File.Create(zipFile))
        {
            var total = (int)((response.Content.Headers.ContentLength ?? 0) >> 20);
            var buffer = new byte[1 << 16];
            long read = 0;
            int n, reported = -1;
            while ((n = source.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, n);
                read += n;
                if ((int)(read >> 20) == reported) continue;
                reported = (int)(read >> 20);
                report(reported, total);
            }
        }
        using (var zip = ZipFile.OpenRead(zipFile))
        {
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("ffmpeg_missing");
            Directory.CreateDirectory(Settings.Folder);
            entry.ExtractToFile(target + ".part", true);
        }
        File.Move(target + ".part", target, true);
        File.Delete(zipFile);
        return target;
    }
}
