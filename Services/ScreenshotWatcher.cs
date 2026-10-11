using System.IO;
using Timer = System.Threading.Timer;

namespace Clothesline.Services;

/// <summary>
/// Watches a folder for new screenshots/images and reports them.
/// On Windows the Screenshots folder (Pictures\Screenshots) is the default
/// target of Win+PrintScreen. We also watch the Desktop.
/// </summary>
internal sealed class ScreenshotWatcher : IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic"
    };

    private static readonly HashSet<string> RecordingExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".wmv"
    };

    private readonly FileSystemWatcher _watcher;
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _onNew;
    private readonly Action _onChange;
    private readonly DateTime _launchDate = DateTime.Now;
    private bool _disposed;

    public string Folder { get; }

    public ScreenshotWatcher(string folder, Action<string> onNew, Action onChange)
    {
        Folder = folder;
        _onNew = onNew;
        _onChange = onChange;
        try { Directory.CreateDirectory(folder); } catch { }
        _watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = false
        };
        _watcher.Created += (s, e) => ScheduleScan();
        _watcher.Renamed += (s, e) => ScheduleScan();
        _watcher.Changed += (s, e) => ScheduleScan();
        _watcher.Deleted += (s, e) => ScheduleScan();
    }

    private Timer? _scanTimer;

    private void ScheduleScan()
    {
        _scanTimer?.Dispose();
        _scanTimer = new Timer(_ => Scan(), null, 200, Timeout.Infinite);
    }

    public void Start()
    {
        try
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
            // Adopt everything already there.
            foreach (var f in ListFiles()) _known.Add(f);
            _watcher.EnableRaisingEvents = true;
        }
        catch { /* folder not accessible */ }
    }

    private List<string> ListFiles()
    {
        try
        {
            return Directory.EnumerateFiles(Folder)
                .Where(IsCandidate)
                .OrderBy(f =>
                {
                    try { return File.GetCreationTime(f); } catch { return DateTime.MinValue; }
                })
                .ToList();
        }
        catch { return new List<string>(); }
    }

    private static bool IsCandidate(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext) || RecordingExtensions.Contains(ext);
    }

    private void Scan()
    {
        if (_disposed) return;
        var files = ListFiles();
        var current = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        // New files: not in known, created after launch (so we don't re-hang
        // every file at startup).
        foreach (var f in files)
        {
            if (_known.Contains(f)) continue;
            try
            {
                if (File.GetCreationTime(f) >= _launchDate)
                    _onNew(f);
            }
            catch { }
        }

        // Removed files: in known but not now.
        bool changed = false;
        foreach (var old in _known)
            if (!current.Contains(old)) { changed = true; break; }

        _known.Clear();
        foreach (var f in files) _known.Add(f);
        if (changed) _onChange();
    }

    public void PruneKnown(Predicate<string> stillExists)
    {
        _known.RemoveWhere(p => !stillExists(p));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scanTimer?.Dispose();
        try { _watcher.EnableRaisingEvents = false; } catch { }
        _watcher.Dispose();
    }
}
