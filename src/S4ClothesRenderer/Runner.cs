using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace S4ClothesRenderer;

public sealed class Job
{
    public ClothingItem Item;
    public ObjectItem Obj;
    public int Paint = -1;
    public Library Lib;
    public FileRef Texture;
    public Component Comp;
    public int Size;
    public string Out;
    public string Label;
    public string Source;
    public int Seq;
    public (int Sex, int Cat, int Index, string Collection, int Num, char Letter) Sort;
}

public sealed class JobOptions
{
    public bool UseGame { get; set; }
    public List<string> Folders { get; set; } = [];
    public string Out { get; set; } = "";
    public string Naming { get; set; } = "cat";
    public string Colors { get; set; } = "first";
    public int Size { get; set; } = 512;
    public bool Force { get; set; }
    public bool Head { get; set; }
    public string Pose { get; set; } = "files";
    public string Format { get; set; } = "gif";
    public List<string> Sexes { get; set; } = [];
    public List<string> Cats { get; set; } = [];
    public string Sex { get; set; } = "";
    public string Cat { get; set; } = "";
    public string Kind { get; set; } = "clothes";
    public List<string> Groups { get; set; } = [];
    public string Group { get; set; } = "";
}

public sealed class RunState
{
    public int Total;
    public int Done;
    public int Failed;
    public int Skipped;
    public int NoTexture;
    public int Empty;
    public string Current = "";
    public bool Finished;
    public bool Stopping;
    public Stopwatch Clock = Stopwatch.StartNew();
}

public sealed class Runner
{
    private const int MaxTexture = 1024;
    private static readonly int[] Sizes = [256, 384, 512, 768, 1024];

    private readonly Action<string, object> send;
    private readonly object scanLock = new();
    private (string Key, List<ClothingItem> Items) lastScan;
    private readonly object objectLock = new();
    private (string Key, Library Lib, List<ObjectItem> Items) lastObjects;
    private readonly ConcurrentDictionary<string, List<Mesh>> heads = new();
    private readonly ConcurrentDictionary<string, Rig> rigs = new();

    public GameArchive Game { get; private set; }
    public string GameStatus { get; private set; } = "";
    public string GameError { get; private set; } = "";
    public bool GameLoading { get; private set; }
    public RunState Run { get; private set; }

    public Runner(Action<string, object> send) => this.send = send;

    public bool GameReady(string path) => Game != null && !GameLoading && string.Equals(Game.Folder, path, StringComparison.OrdinalIgnoreCase);

    public void LoadGame(string path)
    {
        if (GameLoading || GameReady(path)) return;
        if (!GameArchive.IsGameFolder(path))
        {
            GameError = "game_invalid";
            send("game", GameInfo(path));
            return;
        }
        GameLoading = true;
        GameError = "";
        Task.Run(() =>
        {
            var archive = new GameArchive(path);
            var last = Stopwatch.StartNew();
            try
            {
                archive.Load(status =>
                {
                    GameStatus = status;
                    if (last.ElapsedMilliseconds < 150) return;
                    last.Restart();
                    send("game", GameInfo(path));
                });
                Game = archive;
                lock (scanLock) lastScan = default;
            }
            catch (Exception e)
            {
                GameError = e.Message;
            }
            GameLoading = false;
            GameStatus = "";
            send("game", GameInfo(path));
        });
    }

    public object GameInfo(string path) => new
    {
        path,
        valid = GameArchive.IsGameFolder(path),
        loading = GameLoading,
        loaded = GameReady(path),
        status = GameStatus,
        error = GameError,
        files = GameReady(path) ? Game.Files.Count : 0,
    };

    private List<ClothingItem> Items(bool useGame, List<string> folders)
    {
        var game = useGame && Game != null && !GameLoading ? Game : null;
        var key = (game?.Folder ?? "") + "|" + string.Join("|", folders);
        lock (scanLock)
        {
            if (lastScan.Key == key) return lastScan.Items;
            var roots = new List<List<(string, FileRef)>>();
            if (game != null) roots.Add(game.Files);
            roots.AddRange(folders.Select(Catalog.ReadFolder));
            lastScan = (key, Catalog.Build(roots));
            return lastScan.Items;
        }
    }

    private (Library Lib, List<ObjectItem> Items) ObjectsFor(bool useGame, List<string> folders)
    {
        var game = useGame && Game != null && !GameLoading ? Game : null;
        var key = (game?.Folder ?? "") + "|" + string.Join("|", folders);
        lock (objectLock)
        {
            if (lastObjects.Key == key) return (lastObjects.Lib, lastObjects.Items);
            var local = new ContentIndex();
            for (var i = 0; i < folders.Count; i++)
            {
                if (!Directory.Exists(folders[i])) continue;
                foreach (var file in Directory.EnumerateFiles(folders[i], "*.*", SearchOption.AllDirectories)) local.Add(Path.GetFileName(file), new FileRef(file), 10 + i);
            }
            var lib = new Library(local, game?.Index);
            lastObjects = (key, lib, [.. Objects.Build(lib), .. Emotes.Catalog(lib, folders), .. Skin.Catalog(game, lib)]);
            return (lastObjects.Lib, lastObjects.Items);
        }
    }

    public void Invalidate()
    {
        lock (scanLock) lastScan = default;
        lock (objectLock) lastObjects = default;
    }

    public object Scan(JobOptions o)
    {
        var counts = new Dictionary<string, Dictionary<string, int[]>> { ["male"] = [], ["female"] = [] };
        o.Kind = "clothes";
        o.Sexes = ["male", "female"];
        o.Cats = [.. Component.All.Select(c => c.Key)];
        o.Colors = "first";
        var models = BuildJobs(o).Jobs;
        o.Colors = "all";
        var variants = BuildJobs(o).Jobs;
        if (models != null)
        {
            foreach (var (jobs, slot) in new[] { (models, 0), (variants, 1) })
            {
                foreach (var j in jobs)
                {
                    if (!counts[j.Item.Sex].TryGetValue(j.Item.Comp, out var c)) counts[j.Item.Sex][j.Item.Comp] = c = new int[2];
                    c[slot]++;
                }
            }
        }
        else
        {
            foreach (var it in Items(o.UseGame, o.Folders))
            {
                if (!counts[it.Sex].TryGetValue(it.Comp, out var c)) counts[it.Sex][it.Comp] = c = new int[2];
                c[0]++;
                c[1] += Math.Max(1, it.Colors.Count);
            }
        }
        var objects = Objects.Kinds.ToDictionary(k => k, _ => new SortedDictionary<string, int>());
        var paints = new SortedDictionary<string, int>();
        var (lib, items) = ObjectsFor(o.UseGame, o.Folders);
        foreach (var it in items)
        {
            objects[it.Kind][it.Group] = objects[it.Kind].GetValueOrDefault(it.Group) + 1;
            if (it.Kind == "vehicles") paints[it.Group] = paints.GetValueOrDefault(it.Group) + Math.Max(1, lib.PaintCombos.GetValueOrDefault(it.Model)?.Count ?? 0);
        }
        return new { counts, objects, paints };
    }

    private List<Job> ObjectJobs(JobOptions o)
    {
        var (lib, items) = ObjectsFor(o.UseGame, o.Folders);
        var size = Sizes.Contains(o.Size) ? o.Size : 512;
        var kindOrder = Array.IndexOf(Objects.Kinds, o.Kind);
        var format = Emotes.Formats.Contains(o.Format) ? o.Format : "gif";
        return [.. items.Where(i => i.Kind == o.Kind && o.Groups.Contains(i.Group))
            .SelectMany(i =>
            {
                var combos = i.Kind == "vehicles" ? lib.PaintCombos.GetValueOrDefault(i.Model)?.Count ?? 0 : 0;
                var variants = o.Colors == "all" && combos > 0;
                var paints = combos == 0 ? [-1] : variants ? Enumerable.Range(0, combos) : [Objects.DefaultPaint(lib, i.Model)];
                var file = string.Concat(i.Name.Split(Path.GetInvalidFileNameChars()));
                return paints.Select(p => new Job
                {
                    Obj = i, Lib = lib, Size = size, Paint = p,
                    Out = i.Kind == "emotes" ? Path.Combine(o.Out, i.Kind, i.Group, file + "." + format)
                        : Skin.Kinds.Contains(i.Kind) ? Path.Combine(o.Out, i.Kind, i.Group, file + ".png")
                        : Path.Combine(o.Out, i.Kind, file + (variants ? "_" + p : "") + ".png"),
                    Label = variants ? i.Name + "_" + p : i.Name, Source = i.Label != i.Name && i.Label != "" ? i.Label : i.Group,
                    Sort = (2, kindOrder, 0, i.Group + "\u0001" + i.Name.ToLowerInvariant(), Math.Max(p, 0), 'a'),
                });
            })
            .OrderBy(j => j.Sort)];
    }

    public static string RefsFile(string outDir) => string.IsNullOrWhiteSpace(outDir) ? null : Path.Combine(outDir, "refs.json");

    private static Dictionary<string, Dictionary<string, List<(string C, int S)>>> ReadRefs(string outDir)
    {
        var file = RefsFile(outDir);
        if (file == null || !File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var refs = new Dictionary<string, Dictionary<string, List<(string, int)>>>();
            foreach (var sex in doc.RootElement.EnumerateObject())
            {
                if (sex.Value.ValueKind != JsonValueKind.Object) continue;
                var cats = refs[sex.Name] = [];
                foreach (var cat in sex.Value.EnumerateObject())
                {
                    if (cat.Value.ValueKind != JsonValueKind.Array) continue;
                    cats[cat.Name] = [.. cat.Value.EnumerateArray()
                        .Where(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("s", out _))
                        .Select(r => ((r.TryGetProperty("c", out var c) ? c.ToString() : "").ToLowerInvariant(), r.GetProperty("s").GetInt32()))];
                }
            }
            return refs;
        }
        catch
        {
            return null;
        }
    }

    public static bool HasRefs(string outDir) => ReadRefs(outDir) != null;

    private (List<Job> Jobs, int Unknown, string Error) BuildJobs(JobOptions o)
    {
        if (o.Kind != "clothes") return (ObjectJobs(o), 0, null);
        var refs = ReadRefs(o.Out);
        if (o.Naming == "game" && refs == null) return (null, 0, "refs_missing");
        var size = Sizes.Contains(o.Size) ? o.Size : 512;
        var jobs = new Dictionary<string, Job>();
        var unknown = 0;
        foreach (var it in Items(o.UseGame, o.Folders))
        {
            if (!o.Sexes.Contains(it.Sex) || !o.Cats.Contains(it.Comp)) continue;
            var comp = Component.ByKey[it.Comp];
            int? index = null;
            if (refs != null)
            {
                var names = it.Collection != "" ? new[] { it.Collection } : ["", it.Model, it.Model + it.PropSuffix];
                if (refs.TryGetValue(it.Sex, out var cats) && cats.TryGetValue(comp.Cat, out var list) && list.FirstOrDefault(r => names.Contains(r.C)) is { C: not null } entry)
                {
                    index = entry.S + it.Num;
                }
                else if (it.Drawable.Entry != null)
                {
                    continue;
                }
                else
                {
                    unknown++;
                }
            }
            var colors = it.Colors.Count == 0 ? [new ColorVariant('a', null)] : o.Colors == "all" ? it.Colors : [it.Colors[0]];
            foreach (var color in colors)
            {
                var suffix = o.Colors == "all" ? "_" + (color.Letter - 'a') : "";
                var folder = Path.Combine(it.Sex, o.Naming == "game" ? comp.Slot : comp.Folder);
                var rel = index != null ? Path.Combine(folder, index + suffix + ".png")
                    : Path.Combine(folder, it.Collection == "" ? "base" : it.Collection, it.Name + "_" + color.Letter + ".png");
                jobs[rel] = new Job
                {
                    Item = it, Texture = color.Texture, Comp = comp, Size = size, Out = Path.Combine(o.Out, rel),
                    Label = Path.GetFileNameWithoutExtension(rel), Source = (it.Collection != "" ? it.Collection + " · " : "") + it.Name + "_" + color.Letter,
                    Sort = (it.Sex == "male" ? 0 : 1, Component.Order[it.Comp], index ?? int.MaxValue, index == null ? it.Collection : "", it.Num, color.Letter),
                };
            }
        }
        return ([.. jobs.Values.OrderBy(j => j.Sort)], unknown, null);
    }

    public static string ImageUrl(string file) => "/img?f=" + Uri.EscapeDataString(file) + "&t=" + File.GetLastWriteTimeUtc(file).Ticks;

    public object Category(JobOptions o)
    {
        o.Sexes = [o.Sex];
        o.Cats = [o.Cat];
        o.Groups = [o.Group];
        var (jobs, _, error) = BuildJobs(o);
        if (error != null) return new { error };
        return new { items = jobs.Select(j => new { j.Out, j.Label, j.Source, url = File.Exists(j.Out) ? ImageUrl(j.Out) : null }) };
    }

    public object Summary(JobOptions o)
    {
        if (o.Kind != "clothes")
        {
            o.Groups = [.. ObjectsFor(o.UseGame, o.Folders).Items.Where(i => i.Kind == o.Kind).Select(i => i.Group).Distinct()];
            var groups = new SortedDictionary<string, int[]>();
            foreach (var j in ObjectJobs(o))
            {
                if (!groups.TryGetValue(j.Obj.Group, out var g)) groups[j.Obj.Group] = g = new int[2];
                g[0]++;
                if (File.Exists(j.Out)) g[1]++;
            }
            return new { groups, refs = HasRefs(o.Out) };
        }
        o.Sexes = ["male", "female"];
        o.Cats = [.. Component.All.Select(c => c.Key)];
        var refs = HasRefs(o.Out);
        var (jobs, unknown, error) = BuildJobs(o);
        if (error != null) return new { error, refs };
        var counts = new Dictionary<string, Dictionary<string, int[]>> { ["male"] = [], ["female"] = [] };
        foreach (var j in jobs)
        {
            if (!counts[j.Item.Sex].TryGetValue(j.Item.Comp, out var c)) counts[j.Item.Sex][j.Item.Comp] = c = new int[2];
            c[0]++;
            if (File.Exists(j.Out)) c[1]++;
        }
        return new { counts, refs, unknown };
    }

    public object Progress()
    {
        var r = Run;
        if (r == null) return null;
        var processed = r.Done + r.Failed;
        var elapsed = r.Clock.Elapsed.TotalSeconds;
        int? eta = !r.Finished && processed >= 3 ? (int)Math.Round((r.Total - processed) / (processed / elapsed)) : null;
        return new { total = r.Total, done = r.Done, failed = r.Failed, skipped = r.Skipped, noTexture = r.NoTexture, empty = r.Empty, current = r.Current, eta, elapsed = (int)elapsed, finished = r.Finished, stopped = r.Stopping };
    }

    public void Stop()
    {
        if (Run != null) Run.Stopping = true;
    }

    public object Start(JobOptions o)
    {
        if (Run != null && !Run.Finished) return new { error = "busy" };
        if (string.IsNullOrWhiteSpace(o.Out)) return new { error = "no_output" };
        if (o.UseGame && GameLoading) return new { error = "game_loading" };
        var (jobs, unknown, error) = BuildJobs(o);
        if (error != null) return new { error };
        var todo = o.Force ? jobs : jobs.Where(j => !File.Exists(j.Out)).ToList();
        for (var i = 0; i < todo.Count; i++) todo[i].Seq = i;
        var run = Run = new RunState { Total = todo.Count, Skipped = jobs.Count - todo.Count };
        if (unknown > 0) send("log", new { key = "unknown_sorted", n = unknown });
        Task.Run(() => Execute(run, todo, o));
        return new { total = todo.Count, skipped = run.Skipped };
    }

    private List<Mesh> Head(string sex)
    {
        var game = Game;
        if (game == null || GameLoading) return [];
        return heads.GetOrAdd(game.Folder + "|" + sex, _ =>
        {
            var prefix = sex == "male" ? "mp_m_freemode_01^" : "mp_f_freemode_01^";
            var model = game.Files.FirstOrDefault(f => f.FullName == prefix + "head_000_r.ydd").File;
            var texture = game.Files.FirstOrDefault(f => f.FullName.StartsWith(prefix + "head_diff_000_a", StringComparison.Ordinal)).File;
            if (model == null || texture == null) return [];
            var tex = Assets3D.ReadTexture(texture.LoadTextures(), MaxTexture);
            if (tex == null) return [];
            var meshes = Assets3D.ReadMeshes(model.LoadDrawables());
            foreach (var m in meshes) m.Tex = tex;
            return meshes;
        });
    }

    private Rig ArmsDown(string sex)
    {
        var game = Game;
        if (game == null || GameLoading) return null;
        return rigs.GetOrAdd(game.Folder + "|" + sex, _ =>
        {
            try
            {
                var file = game.Index.Get(sex == "male" ? "mp_m_freemode_01.yft" : "mp_f_freemode_01.yft");
                return file == null ? null : Rig.ArmsDown(file.LoadFragment().Fragment?.Drawable?.Skeleton);
            }
            catch
            {
                return null;
            }
        });
    }

    private static List<Mesh> LoadMeshes(Job job)
    {
        var meshes = job.Obj != null ? Objects.Scene(job.Obj, job.Lib, MaxTexture) : Assets3D.ReadMeshes(job.Item.Drawable.LoadDrawables());
        if (meshes.Count == 0) throw new InvalidDataException("no mesh");
        return meshes;
    }

    private void Execute(RunState run, List<Job> todo, JobOptions o)
    {
        var lastProgress = Stopwatch.StartNew();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, o.Kind == "emotes" ? Environment.ProcessorCount / 2 : Environment.ProcessorCount - 1) };
        var posed = o.Kind == "clothes" && o.Pose == "down" ? new[] { "male", "female" }.ToDictionary(s => s, ArmsDown) : null;
        if (posed != null && posed.Values.Any(r => r == null)) send("log", new { key = "pose_unavailable" });
        var game = Game;
        var format = Emotes.Formats.Contains(o.Format) ? o.Format : "gif";
        string ffmpeg = null;
        if (todo.Any(j => j.Obj?.Kind == "emotes"))
        {
            try
            {
                ffmpeg = Emotes.Ffmpeg((done, total) =>
                {
                    run.Current = $"ffmpeg {done} / {total} MB";
                    send("progress", Progress());
                });
            }
            catch (Exception e)
            {
                send("log", new { key = "failed", label = "ffmpeg", message = e.Message });
            }
        }
        send("progress", Progress());
        var groups = Partitioner.Create(todo.GroupBy(j => (object)j.Item ?? j.Obj).ToList(), EnumerablePartitionerOptions.NoBuffering);
        Parallel.ForEach(groups, options, () => new RenderBuffers(), (group, state, buffers) =>
        {
            List<Mesh> meshes = null;
            var placeholder = false;
            foreach (var job in group)
            {
                if (run.Stopping)
                {
                    state.Stop();
                    break;
                }
                run.Current = job.Label;
                try
                {
                    Tex tex = null;
                    if (job.Obj?.Kind == "emotes")
                    {
                        Emotes.Capture(game, ffmpeg, job.Lib, job.Obj, job.Out, job.Size, format);
                    }
                    else
                    {
                        byte[] rgba = null;
                        if (Skin.Kinds.Contains(job.Obj?.Kind))
                        {
                            rgba = Skin.Render(game, job.Lib, job.Obj, job.Size, buffers);
                        }
                        else
                        {
                            if (meshes == null)
                            {
                                meshes = LoadMeshes(job);
                                placeholder = job.Obj == null && Renderer.IsPlaceholder(meshes);
                                if (posed != null && !placeholder && !job.Comp.Prop) posed[job.Item.Sex]?.Apply(meshes);
                            }
                            if (job.Obj != null)
                            {
                                Objects.Paint(meshes, job.Lib, job.Obj.Model, job.Paint);
                                rgba = Renderer.Render(meshes, null, Objects.CameraFor(job.Obj.Kind), job.Size, false, buffers);
                            }
                            else
                            {
                                var head = o.Head && job.Comp.Key == "hair" ? Head(job.Item.Sex) : [];
                                if (!placeholder || head.Count > 0)
                                {
                                    tex = job.Texture != null ? Assets3D.ReadTexture(job.Texture.LoadTextures(), Math.Min(MaxTexture, job.Size * 2)) : null;
                                    if (tex == null) foreach (var m in meshes) m.Tex ??= Assets3D.Decode(m.Embedded, Math.Min(MaxTexture, job.Size * 2));
                                    rgba = head.Count > 0
                                        ? Renderer.Render(placeholder ? head : [.. head, .. meshes], tex, Camera.Toward(0.35f, -1, 0.05f), job.Size, false, buffers)
                                        : Renderer.Render(meshes, tex, Camera.Of(job.Comp.View), job.Size, job.Comp.Key == "hair", buffers);
                                }
                            }
                        }
                        if (rgba == null)
                        {
                            rgba = new byte[job.Size * job.Size * 4];
                            Interlocked.Increment(ref run.Empty);
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(job.Out));
                        File.WriteAllBytes(job.Out, Png.Encode(rgba, job.Size, job.Size));
                    }
                    Interlocked.Increment(ref run.Done);
                    if (job.Obj == null && !placeholder && tex == null && meshes.All(m => m.Tex == null)) Interlocked.Increment(ref run.NoTexture);
                    send("image", new { url = ImageUrl(job.Out), @out = job.Out, label = job.Label, source = job.Source, seq = job.Seq, sex = job.Item?.Sex, comp = job.Item?.Comp, kind = job.Obj?.Kind ?? "clothes", group = job.Obj?.Group });
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref run.Failed);
                    send("log", new { key = "failed", label = job.Source, message = e.Message });
                }
                lock (lastProgress)
                {
                    if (lastProgress.ElapsedMilliseconds < 100) continue;
                    lastProgress.Restart();
                }
                send("progress", Progress());
            }
            return buffers;
        }, _ => { });
        if (run.Empty > 0) send("log", new { key = "empty_slots", n = run.Empty });
        if (run.NoTexture > 0) send("log", new { key = "no_texture", n = run.NoTexture });
        run.Finished = true;
        send("progress", Progress());
        send("end", new { });
    }
}
