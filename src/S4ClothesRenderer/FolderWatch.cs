namespace S4ClothesRenderer;

public sealed class FolderWatch : IDisposable
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { "", ".ydd", ".ytd", ".ydr", ".yft", ".ytyp", ".meta", ".ymt" };
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly System.Threading.Timer timer;

    public FolderWatch(IEnumerable<string> folders, Action changed)
    {
        timer = new System.Threading.Timer(_ => changed(), null, Timeout.Infinite, Timeout.Infinite);
        foreach (var folder in folders.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Created += (_, e) => Changed(e.FullPath);
                watcher.Deleted += (_, e) => Changed(e.FullPath);
                watcher.Changed += (_, e) => Changed(e.FullPath);
                watcher.Renamed += (_, e) =>
                {
                    Changed(e.FullPath);
                    Changed(e.OldFullPath);
                };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch
            {
            }
        }
    }

    private void Changed(string path)
    {
        if (Extensions.Contains(Path.GetExtension(path))) timer.Change(1000, Timeout.Infinite);
    }

    public void Dispose()
    {
        foreach (var watcher in watchers) watcher.Dispose();
        timer.Dispose();
    }
}
