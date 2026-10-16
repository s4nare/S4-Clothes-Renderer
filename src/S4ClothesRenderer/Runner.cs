using System.Diagnostics;
using System.Text.Json;

namespace S4ClothesRenderer;

public sealed class Job
{
    public ClothingItem Item;
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
    public List<string> Sexes { get; set; } = [];
    public List<string> Cats { get; set; } = [];
    public string Sex { get; set; } = "";
    public string Cat { get; set; } = "";
}

public sealed class RunState
{
    public int Total;
    public int Done;
    public int Failed;
    public int Skipped;
    public int NoTexture;
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

    public void Invalidate()
    {
        lock (scanLock) lastScan = default;
    }

    public object Scan(JobOptions o)
    {
        var counts = new Dictionary<string, Dictionary<string, int[]>> { ["male"] = [], ["female"] = [] };
        foreach (var it in Items(o.UseGame, o.Folders))
        {
            if (!counts[it.Sex].TryGetValue(it.Comp, out var c)) counts[it.Sex][it.Comp] = c = new int[2];
            c[0]++;
            c[1] += Math.Max(1, it.Colors.Count);
        }
        return new { counts };
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
                else
                {
                    unknown++;
                }
            }
            if (o.Naming == "game" && index == null) continue;
            var colors = it.Colors.Count == 0 ? [new ColorVariant('a', null)] : o.Colors == "all" ? it.Colors : [it.Colors[0]];
            foreach (var color in colors)
            {
                var suffix = o.Colors == "all" ? "_" + (color.Letter - 'a') : "";
                var folder = Path.Combine(it.Sex, comp.Slot + " - " + comp.Cat);
                var rel = o.Naming == "game" ? Path.Combine(it.Sex, comp.Slot, index + suffix + ".png")
                    : index != null ? Path.Combine(folder, index + suffix + ".png")
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
        var (jobs, _, error) = BuildJobs(o);
        if (error != null) return new { error };
        return new { items = jobs.Select(j => new { j.Out, j.Label, j.Source, url = File.Exists(j.Out) ? ImageUrl(j.Out) : null }) };
    }

    public object Summary(JobOptions o)
    {
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
        return new { total = r.Total, done = r.Done, failed = r.Failed, skipped = r.Skipped, noTexture = r.NoTexture, current = r.Current, eta, elapsed = (int)elapsed, finished = r.Finished, stopped = r.Stopping };
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
        if (unknown > 0) send("log", new { key = o.Naming == "game" ? "unknown_skipped" : "unknown_sorted", n = unknown });
        Task.Run(() => Execute(run, todo));
        return new { total = todo.Count, skipped = run.Skipped };
    }

    private void Execute(RunState run, List<Job> todo)
    {
        var lastProgress = Stopwatch.StartNew();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
        Parallel.ForEach(todo, options, (job, state) =>
        {
            if (run.Stopping)
            {
                state.Stop();
                return;
            }
            run.Current = job.Label;
            try
            {
                var tex = job.Texture != null ? Assets3D.ReadTexture(job.Texture.LoadTextures(), MaxTexture) : null;
                var meshes = Assets3D.ReadMeshes(job.Item.Drawable.LoadDrawables());
                if (meshes.Count == 0) throw new InvalidDataException("no mesh");
                var rgba = Renderer.Render(meshes, tex, Camera.Of(job.Comp.View), job.Size, job.Comp.Key == "hair");
                Directory.CreateDirectory(Path.GetDirectoryName(job.Out));
                File.WriteAllBytes(job.Out, Png.Encode(rgba, job.Size, job.Size));
                Interlocked.Increment(ref run.Done);
                if (tex == null) Interlocked.Increment(ref run.NoTexture);
                send("image", new { url = ImageUrl(job.Out), @out = job.Out, label = job.Label, source = job.Source, seq = job.Seq, sex = job.Item.Sex, comp = job.Item.Comp });
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref run.Failed);
                send("log", new { key = "failed", label = job.Source, message = e.Message });
            }
            lock (lastProgress)
            {
                if (lastProgress.ElapsedMilliseconds < 100) return;
                lastProgress.Restart();
            }
            send("progress", Progress());
        });
        run.Finished = true;
        send("progress", Progress());
        send("end", new { });
    }
}
