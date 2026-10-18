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
            Diag.Write("watch", $"watching {Folder} ({_known.Count} pre-existing)");
        }
        catch (Exception ex) { Diag.Error("watch", ex); }
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
        // "<name>-annotated.png" is what the annotation window writes when it
        // cannot replace the original in place. That is an edit of something
        // already on the line, not a new capture, so it must never hang again.
        if (Path.GetFileNameWithoutExtension(path)
                .EndsWith("-annotated", StringComparison.OrdinalIgnoreCase))
            return false;

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
                {
                    Diag.Write("watch", $"new file -> {Path.GetFileName(f)}");
                    _onNew(f);
                }
            }
            catch (Exception ex) { Diag.Error("watch", ex); }
        }

        // Removed files: in known but not now.
        bool changed = false;
        foreach (var old in _known)
        {
            if (current.Contains(old)) continue;
            changed = true;
            Diag.Write("watch", $"vanished -> {Path.GetFileName(old)}");
        }

        _known.Clear();
        foreach (var f in files) _known.Add(f);
        if (changed)
        {
            // Line.Prune marshals to the UI thread and re-raises anything the
            // UI throws. This callback runs on a timer thread with no handler,
            // so an exception here would take the whole process down.
            try { _onChange(); }
            catch (Exception ex) { Diag.Error("watch", ex); }
        }
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
