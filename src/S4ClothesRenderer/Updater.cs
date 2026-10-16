using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace S4ClothesRenderer;

public sealed class Updater
{
    private const string Repo = "s4nare/S4-Clothes-Renderer";
    private static readonly string Exe = Environment.ProcessPath;
    public static readonly string Current = typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly bool Enabled = typeof(Updater).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration == "Release";
    private readonly Action<string, object> send;

    public object Pending { get; private set; }

    public Updater(Action<string, object> send) => this.send = send;

    public async Task Run(Settings settings)
    {
        if (!Enabled) return;
        try
        {
            File.Delete(Exe + ".old");
        }
        catch
        {
        }
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("S4ClothesRenderer/" + Current);
            var seen = settings.SeenVersion;
            if (seen != Current)
            {
                settings.SeenVersion = Current;
                settings.Save();
                if (!string.IsNullOrEmpty(seen))
                {
                    try
                    {
                        Publish(Current, Notes(await Release(http, "tags/v" + Current)), "installed", "");
                    }
                    catch
                    {
                    }
                }
            }
            var latest = await Release(http, "latest");
            var tag = latest.GetProperty("tag_name").GetString().TrimStart('v');
            if (!Version.TryParse(tag, out var next) || next <= Version.Parse(Current)) return;
            var notes = Notes(latest);
            var page = latest.GetProperty("html_url").GetString();
            var asset = latest.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind != JsonValueKind.Object)
            {
                Publish(tag, notes, "manual", page);
                return;
            }
            Publish(tag, notes, "downloading", page);
            try
            {
                var zip = new ZipArchive(new MemoryStream(await http.GetByteArrayAsync(asset.GetProperty("browser_download_url").GetString())));
                var entry = zip.Entries.First(e => e.Name.Equals(Path.GetFileName(Exe), StringComparison.OrdinalIgnoreCase));
                entry.ExtractToFile(Exe + ".new", true);
                File.Move(Exe, Exe + ".old", true);
                File.Move(Exe + ".new", Exe);
                Publish(tag, notes, "ready", page);
            }
            catch
            {
                if (!File.Exists(Exe) && File.Exists(Exe + ".old")) File.Move(Exe + ".old", Exe);
                Publish(tag, notes, "manual", page);
            }
        }
        catch
        {
        }
    }

    public static void Restart()
    {
        Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = false });
        Application.Exit();
    }

    private static async Task<JsonElement> Release(HttpClient http, string which) =>
        JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/{which}")).RootElement.Clone();

    private static string Notes(JsonElement release) =>
        release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : "";

    private void Publish(string version, string notes, string state, string url)
    {
        Pending = new { version, notes, state, url };
        send("update", Pending);
    }
}
