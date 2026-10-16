using System.Diagnostics;
using System.Text.Json;
using System.Web;

namespace S4ClothesRenderer;

public sealed record ApiResponse(int Status, string ContentType, Stream Body)
{
    public static ApiResponse Of(object value) => new(200, "application/json; charset=utf-8", new MemoryStream(Json.Bytes(value)));
    public static ApiResponse Bytes(byte[] data, string type) => data == null ? NotFound : new(200, type, new MemoryStream(data));
    public static readonly ApiResponse NotFound = new(404, "text/plain", new MemoryStream());
}

public sealed class Api
{
    private static readonly Dictionary<string, string> Types = new()
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".woff2"] = "font/woff2", [".png"] = "image/png", [".svg"] = "image/svg+xml",
    };

    private readonly Branding branding = Branding.Load();
    private readonly Settings settings = Settings.Load();
    private readonly Func<Func<string>, string> onUi;
    private readonly Func<string, bool, string> pickFolder;
    private readonly Func<string, string> pickFile;
    private readonly Action<string, object> send;
    private FileSystemWatcher watcher;
    private FolderWatch sources;
    private readonly Updater updater;
    public Runner Runner { get; }

    public Api(Action<string, object> send, Func<Func<string>, string> onUi, Func<string, bool, string> pickFolder, Func<string, string> pickFile)
    {
        this.onUi = onUi;
        this.pickFolder = pickFolder;
        this.pickFile = pickFile;
        Runner = new Runner(send);
        if (string.IsNullOrWhiteSpace(settings.GamePath)) settings.GamePath = GameArchive.Detect() ?? "";
        if (settings.UseGame && GameArchive.IsGameFolder(settings.GamePath)) Runner.LoadGame(settings.GamePath);
        this.send = send;
        SyncRefs();
        Watch();
        WatchSources();
        updater = new Updater(send);
        _ = Task.Run(() => updater.Run(settings));
    }

    public string Title => branding.Name;

    public ApiResponse Handle(string path, string query, Stream body)
    {
        try
        {
            if (!path.StartsWith("/api/")) return Static(path, query);
            var json = Read(body);
            return path switch
            {
                "/api/state" => ApiResponse.Of(State()),
                "/api/settings" => ApiResponse.Of(SaveSettings(json)),
                "/api/game" => ApiResponse.Of(LoadGame(json)),
                "/api/scan" => ApiResponse.Of(Runner.Scan(Options(json))),
                "/api/category" => ApiResponse.Of(Runner.Category(Options(json))),
                "/api/summary" => ApiResponse.Of(Runner.Summary(Options(json))),
                "/api/start" => ApiResponse.Of(Runner.Start(Options(json))),
                "/api/stop" => ApiResponse.Of(Stop()),
                "/api/pick" => ApiResponse.Of(Pick(json)),
                "/api/refs" => ApiResponse.Of(ImportRefs(json)),
                "/api/open" => ApiResponse.Of(Open(json)),
                "/api/resource" => ApiResponse.Of(InstallResource(json)),
                "/api/restart" => ApiResponse.Of(Restart()),
                _ => ApiResponse.NotFound,
            };
        }
        catch (Exception e)
        {
            return new ApiResponse(500, "application/json; charset=utf-8", new MemoryStream(Json.Bytes(new { error = e.Message })));
        }
    }

    private static JsonElement Read(Stream body)
    {
        if (body == null) return default;
        using var reader = new StreamReader(body);
        var text = reader.ReadToEnd();
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Str(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

    private JobOptions Options(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object ? json.Deserialize<JobOptions>(Json.Options) : new JobOptions();

    private ApiResponse Static(string path, string query)
    {
        switch (path)
        {
            case "/":
                return ApiResponse.Bytes(Embedded.Read("web/index.html"), Types[".html"]);
            case "/logo":
                return ApiResponse.Bytes(branding.LogoBytes(), "image/png");
            case "/img":
                var file = Path.GetFullPath(HttpUtility.ParseQueryString(query ?? "")["f"] ?? "");
                if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) return ApiResponse.NotFound;
                return new ApiResponse(200, "image/png", new MemoryStream(File.ReadAllBytes(file)));
        }
        var name = path.TrimStart('/');
        return Types.TryGetValue(Path.GetExtension(name), out var type) ? ApiResponse.Bytes(Embedded.Read("web/" + name), type) : ApiResponse.NotFound;
    }

    private object State() => new
    {
        categories = Component.All.Select(c => new { key = c.Key, cat = c.Cat, slot = c.Slot, prop = c.Prop }),
        branding = new { name = branding.Name, color = branding.Color, logoZoom = branding.LogoZoom },
        settings,
        game = Runner.GameInfo(settings.GamePath),
        refs = Runner.HasRefs(settings.Out),
        resource = Directory.Exists(settings.ResourcePath) ? settings.ResourcePath : "",
        progress = Runner.Progress(),
        version = Updater.Current,
        update = updater.Pending,
    };

    private object SaveSettings(JsonElement json)
    {
        var next = json.Deserialize<Settings>(Json.Options);
        if (next == null) return new { };
        settings.UseGame = next.UseGame;
        settings.GamePath = next.GamePath ?? "";
        var folders = settings.Folders;
        settings.Folders = [.. (next.Folders ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (!folders.SequenceEqual(settings.Folders, StringComparer.OrdinalIgnoreCase)) WatchSources();
        settings.Out = next.Out ?? "";
        settings.Naming = next.Naming == "game" ? "game" : "cat";
        settings.Colors = next.Colors == "all" ? "all" : "first";
        settings.Size = next.Size;
        settings.Force = next.Force;
        settings.Head = next.Head;
        settings.Pose = next.Pose == "down" ? "down" : "files";
        settings.Lang = next.Lang ?? "";
        settings.Save();
        SyncRefs();
        return new { refs = Runner.HasRefs(settings.Out) };
    }

    private object Restart()
    {
        Runner.Stop();
        onUi(() =>
        {
            Updater.Restart();
            return "";
        });
        return new { };
    }

    private object InstallResource(JsonElement json)
    {
        var folder = Str(json, "path");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return new { error = "resource_folder" };
        var target = Path.GetFileName(folder.TrimEnd('\\', '/')).Equals("clothes_refs", StringComparison.OrdinalIgnoreCase) ? folder : Path.Combine(folder, "clothes_refs");
        Directory.CreateDirectory(target);
        foreach (var name in new[] { "fxmanifest.lua", "client.lua", "server.lua" })
        {
            File.WriteAllBytes(Path.Combine(target, name), Embedded.Read("clothes_refs/" + name));
        }
        settings.ResourcePath = target;
        settings.Save();
        SyncRefs();
        Watch();
        return new { resource = target, refs = Runner.HasRefs(settings.Out) };
    }

    private bool SyncRefs()
    {
        if (string.IsNullOrWhiteSpace(settings.ResourcePath) || string.IsNullOrWhiteSpace(settings.Out)) return false;
        var source = Path.Combine(settings.ResourcePath, "refs.json");
        var target = Runner.RefsFile(settings.Out);
        if (!File.Exists(source) || (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))) return false;
        try
        {
            using (JsonDocument.Parse(File.ReadAllText(source)))
            {
            }
            Directory.CreateDirectory(settings.Out);
            File.Copy(source, target, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Watch()
    {
        watcher?.Dispose();
        watcher = null;
        if (!Directory.Exists(settings.ResourcePath)) return;
        watcher = new FileSystemWatcher(settings.ResourcePath, "refs.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        FileSystemEventHandler changed = (_, _) => Task.Delay(500).ContinueWith(_ =>
        {
            if (SyncRefs()) send("refs", new { refs = true });
        });
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.EnableRaisingEvents = true;
    }

    private void WatchSources()
    {
        sources?.Dispose();
        sources = new FolderWatch(settings.Folders, () =>
        {
            Runner.Invalidate();
            send("sources", new { });
        });
    }

    private object LoadGame(JsonElement json)
    {
        var path = Str(json, "path");
        if (Str(json, "action") == "detect") path = GameArchive.Detect() ?? "";
        Runner.LoadGame(path);
        return Runner.GameInfo(path);
    }

    private object Stop()
    {
        Runner.Stop();
        return new { };
    }

    private object Pick(JsonElement json)
    {
        var initial = Str(json, "initial");
        var kind = Str(json, "kind");
        var path = kind == "refs" ? onUi(() => pickFile(initial)) : onUi(() => pickFolder(initial, kind != "game"));
        return new { path = path ?? "", valid = kind == "game" && GameArchive.IsGameFolder(path) };
    }

    private object ImportRefs(JsonElement json)
    {
        var source = Str(json, "path");
        var outDir = Str(json, "out");
        if (string.IsNullOrWhiteSpace(outDir)) return new { error = "no_output" };
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(source));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new { error = "refs_invalid" };
        }
        catch
        {
            return new { error = "refs_invalid" };
        }
        Directory.CreateDirectory(outDir);
        var target = Runner.RefsFile(outDir);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(source, target, true);
        return new { refs = Runner.HasRefs(outDir) };
    }

    private static object Open(JsonElement json)
    {
        var path = Str(json, "path");
        if (string.IsNullOrWhiteSpace(path)) return new { };
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        else
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        return new { };
    }
}
