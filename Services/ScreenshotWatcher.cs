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

    /// <summary>
    /// Name shapes that mark a file as a fresh capture rather than some
    /// other image the user happened to save into a watched folder. Matched
    /// case-insensitively against the file name without its extension.
    /// Covers the Windows screenshot tool's own naming plus the common
    /// third-party tools; anything unrecognised is deliberately left alone.
    /// </summary>
    private static readonly string[] ScreenshotNameHints =
    {
        "screenshot", "screen shot", "屏幕截图", "截屏", "screenclip",
        "clipboard", "snipaste", "screen recording", "屏幕录制",
    };

    private readonly FileSystemWatcher _watcher;
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _onNew;
    private readonly Action _onChange;
    private readonly DateTime _launchDate = DateTime.Now;
    private readonly bool _requireScreenshotName;
    private bool _disposed;

    /// <summary>
    /// _known is touched from three places: Start (UI thread), Scan (timer
    /// thread) and Adopt (UI thread, straight after ClipboardWatcher saves a
    /// file into this very folder). Everything that reads or writes it goes
    /// through this gate.
    /// </summary>
    private readonly object _gate = new();

    public string Folder { get; }

    /// <param name="requireScreenshotName">
    /// When true, only files whose name looks like a capture are hung. Used
    /// for folders with mixed content (the Desktop safety watcher); the
    /// Inbox and the system Screenshots folder are dedicated capture
    /// locations, so everything image-shaped in them counts.
    /// </param>
    public ScreenshotWatcher(string folder, Action<string> onNew, Action onChange,
                             bool requireScreenshotName = false)
    {
        Folder = folder;
        _onNew = onNew;
        _onChange = onChange;
        _requireScreenshotName = requireScreenshotName;
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
            lock (_gate)
            {
                foreach (var f in ListFiles()) _known.Add(f);
            }
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
                .Where(f => IsCandidate(f, _requireScreenshotName))
                .OrderBy(f =>
                {
                    try { return File.GetCreationTime(f); } catch { return DateTime.MinValue; }
                })
                .ToList();
        }
        catch { return new List<string>(); }
    }

    private static bool IsCandidate(string path, bool requireScreenshotName)
    {
        // "<name>-annotated.png" is what the annotation window writes when it
        // cannot replace the original in place. That is an edit of something
        // already on the line, not a new capture, so it must never hang again.
        if (Path.GetFileNameWithoutExtension(path)
                .EndsWith("-annotated", StringComparison.OrdinalIgnoreCase))
            return false;

        var ext = Path.GetExtension(path);
        if (!ImageExtensions.Contains(ext) && !RecordingExtensions.Contains(ext))
            return false;

        if (!requireScreenshotName) return true;

        // Name-gated folders (the Desktop): only files that announce
        // themselves as captures hang. Everything else - a photo saved from
        // an editor, a download dropped on the desktop - is left alone, the
        // way the original's screenshot-metadata check left it alone.
        var name = Path.GetFileNameWithoutExtension(path);
        return ScreenshotNameHints.Any(hint =>
            name.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private void Scan()
    {
        if (_disposed) return;
        var files = ListFiles();
        var current = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        // Snapshot the diff under the gate: Adopt runs on the UI thread and
        // would otherwise race this timer-thread scan.
        List<string> fresh;
        List<string> vanished;
        lock (_gate)
        {
            fresh = files.Where(f => !_known.Contains(f)).ToList();
            vanished = _known.Where(old => !current.Contains(old)).ToList();
            _known.Clear();
            foreach (var f in files) _known.Add(f);
        }

        // New files: not in known, created after launch (so we don't re-hang
        // every file at startup).
        foreach (var f in fresh)
        {
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

        foreach (var old in vanished)
        {
            Diag.Write("watch", $"vanished -> {Path.GetFileName(old)}");
        }

        if (vanished.Count > 0)
        {
            // Line.Prune marshals to the UI thread and re-raises anything the
            // UI throws. This callback runs on a timer thread with no handler,
            // so an exception here would take the whole process down.
            try { _onChange(); }
            catch (Exception ex) { Diag.Error("watch", ex); }
        }
    }

    /// <summary>
    /// Marks a path as already handled so a later Scan does not announce it
    /// again. Called right after ClipboardWatcher saves a capture into this
    /// folder: HangCapture hangs it directly, and without this the folder
    /// scan would report the very same file a second time - Hang() dedups
    /// the card, but the second pass still re-runs ComeDown and resets the
    /// peek timer on every single clipboard capture.
    /// </summary>
    public void Adopt(string path)
    {
        var full = Path.GetFullPath(path);
        // Only files this watcher owns may enter _known: a foreign path
        // would never appear in ListFiles, so it would read as "vanished"
        // on every scan and re-run _onChange forever.
        var root = Path.GetFullPath(Folder);
        if (!full.StartsWith(root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
        lock (_gate) _known.Add(full);
    }

    public void PruneKnown(Predicate<string> stillExists)
    {
        lock (_gate) _known.RemoveWhere(p => !stillExists(p));
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
