using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using Microsoft.Win32;

namespace S4ClothesRenderer;

public sealed class GameArchive
{
    private static readonly Regex FreemodeFolder = new(@"^mp_[mf]_freemode_01(_p)?(_.+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Wanted = new(@"(\.ydd|_diff_\d{3}_[a-z](_[a-z]+)?\.ytd)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Folder { get; }
    public bool Gen9 { get; }
    public List<(string FullName, FileRef File)> Files { get; private set; } = [];
    public ContentIndex Index { get; private set; } = new();

    public GameArchive(string folder)
    {
        Folder = folder;
        Gen9 = !File.Exists(Path.Combine(folder, "GTA5.exe")) && File.Exists(Path.Combine(folder, "GTA5_Enhanced.exe"));
    }

    public static bool IsGameFolder(string folder) =>
        !string.IsNullOrWhiteSpace(folder) && (File.Exists(Path.Combine(folder, "GTA5.exe")) || File.Exists(Path.Combine(folder, "GTA5_Enhanced.exe")));

    public static string Detect()
    {
        var candidates = new List<string>();
        var ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app", "CitizenFX.ini");
        if (File.Exists(ini))
        {
            var line = File.ReadAllLines(ini).FirstOrDefault(l => l.StartsWith("IVPath=", StringComparison.OrdinalIgnoreCase));
            if (line != null) candidates.Add(line[7..].Trim());
        }
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V", @"SOFTWARE\WOW6432Node\Rockstar Games\GTAV", @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V Enhanced" })
        {
            using var k = Registry.LocalMachine.OpenSubKey(key);
            foreach (var name in new[] { "InstallFolder", "InstallFolderSteam", "InstallFolderEpic" })
            {
                if (k?.GetValue(name) is string value) candidates.Add(value);
            }
        }
        candidates.Add(@"C:\Program Files\Rockstar Games\Grand Theft Auto V");
        candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V");
        candidates.Add(@"C:\Program Files\Epic Games\GTAV");
        return candidates.Select(c => c.TrimEnd('\\', '/')).FirstOrDefault(IsGameFolder);
    }

    public void Load(Action<string> status)
    {
        status("keys");
        GTA5Keys.LoadFromPath(Folder, Gen9, null);
        var manager = new RpfManager();
        manager.Init(Folder, Gen9, s => status(s), _ => { }, false, false);
        var files = new List<(string, FileRef)>();
        var index = new ContentIndex();
        foreach (var rpf in manager.AllRpfs)
        {
            if (rpf.AllEntries == null) continue;
            foreach (var entry in rpf.AllEntries)
            {
                if (entry is not RpfFileEntry fileEntry) continue;
                var rank = entry.Path.StartsWith("update", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                index.Add(entry.NameLower, new FileRef(fileEntry), rank);
                if (entry is not RpfResourceFileEntry resource || !Wanted.IsMatch(entry.NameLower)) continue;
                var folder = entry.Parent?.NameLower;
                if (folder == null || !FreemodeFolder.IsMatch(folder)) continue;
                files.Add((folder + "^" + entry.NameLower, new FileRef(resource)));
            }
        }
        Files = files;
        Index = index;
    }
}

public sealed class FileRef
{
    public string Path { get; }
    public RpfFileEntry Entry { get; }

    public FileRef(string path) => Path = path;
    public FileRef(RpfFileEntry entry) => Entry = entry;

    public string Display => Entry != null ? Entry.Path : Path;

    public YddFile LoadDrawables()
    {
        var ydd = new YddFile();
        if (Entry != null) ydd.Load(Entry.File.ExtractFile(Entry), Entry);
        else ydd.Load(File.ReadAllBytes(Path));
        return ydd;
    }

    public byte[] Bytes() => Entry != null ? Entry.File.ExtractFile(Entry) : File.ReadAllBytes(Path);

    public YftFile LoadFragment()
    {
        var yft = new YftFile();
        if (Entry != null) yft.Load(Entry.File.ExtractFile(Entry), Entry);
        else yft.Load(File.ReadAllBytes(Path));
        return yft;
    }

    public YdrFile LoadDrawable()
    {
        var ydr = new YdrFile();
        if (Entry != null) ydr.Load(Entry.File.ExtractFile(Entry), Entry);
        else ydr.Load(File.ReadAllBytes(Path));
        return ydr;
    }

    public YtdFile LoadTextures()
    {
        var ytd = new YtdFile();
        if (Entry != null) ytd.Load(Entry.File.ExtractFile(Entry), Entry);
        else ytd.Load(File.ReadAllBytes(Path));
        return ytd;
    }
}
