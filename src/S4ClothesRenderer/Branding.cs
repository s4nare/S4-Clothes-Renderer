using System.Reflection;
using System.Text.Json;

namespace S4ClothesRenderer;

public sealed class Branding
{
    public string Name { get; set; } = "S4 Clothes Renderer";
    public string Color { get; set; } = "#7d7dfa";
    public string Logo { get; set; } = "";
    public double LogoZoom { get; set; } = 1.4;

    private static readonly string AppDir = Path.GetDirectoryName(Environment.ProcessPath);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static Branding Load()
    {
        var file = Path.Combine(AppDir, "branding.json");
        if (!File.Exists(file)) return new Branding();
        try
        {
            return JsonSerializer.Deserialize<Branding>(File.ReadAllText(file), Options) ?? new Branding();
        }
        catch
        {
            return new Branding();
        }
    }

    public byte[] LogoBytes()
    {
        if (!string.IsNullOrWhiteSpace(Logo))
        {
            var file = Path.IsPathRooted(Logo) ? Logo : Path.Combine(AppDir, Logo);
            if (File.Exists(file)) return File.ReadAllBytes(file);
        }
        return Embedded.Read("logo.png");
    }
}

public static class Embedded
{
    public static byte[] Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
