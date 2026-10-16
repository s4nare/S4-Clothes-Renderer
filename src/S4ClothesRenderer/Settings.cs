using System.Text.Json;

namespace S4ClothesRenderer;

public sealed class Settings
{
    public bool UseGame { get; set; } = true;
    public string GamePath { get; set; } = "";
    public List<string> Folders { get; set; } = [];
    public string Out { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "S4 Clothes Renderer");
    public string Naming { get; set; } = "cat";
    public string Colors { get; set; } = "first";
    public int Size { get; set; } = 512;
    public bool Force { get; set; }
    public bool Head { get; set; }
    public string Pose { get; set; } = "down";
    public string Lang { get; set; } = "";
    public string ResourcePath { get; set; } = "";
    public string SeenVersion { get; set; } = "";

    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "S4ClothesRenderer");
    private static readonly string File = Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (System.IO.File.Exists(File)) return JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File), Json.Options) ?? new Settings();
        }
        catch
        {
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, Json.Options));
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = false,
    };

    public static byte[] Bytes(object value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
}
