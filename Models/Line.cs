using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Services;
using Clothesline.UI;

namespace Clothesline.Models;

/// <summary>
/// The line itself: what hangs on it and what you can do with each item.
/// The files never move; the line is only a view onto them. Mirrors the
/// Swift `Line` class.
/// </summary>
internal sealed class Line : INotifyPropertyChanged
{
    public ObservableCollection<PeggedItem> Items { get; } = new();

    public event Action<PeggedItem>? Fall;
    public event Action? ItemsChanged;

    /// <summary>
    /// Raised after a card's thumbnail has been replaced - e.g. the annotation
    /// window rewrote the file in place. PeggedItem is not observable and
    /// PeggedCard assigns Image.Source once, so the card has to be told
    /// explicitly or it keeps showing the pre-edit picture.
    /// </summary>
    public event Action<Guid>? ThumbChanged;

    private int _visibleCount = 8;
    public int VisibleCount
    {
        get => _visibleCount;
        set { _visibleCount = value; OnPropertyChanged(); }
    }

    private int _maxItems = 8;
    public int MaxItems
    {
        get => _maxItems;
        set { _maxItems = value; OnPropertyChanged(); }
    }

    private double _scroll;
    public double Scroll
    {
        get => _scroll;
        set { _scroll = value; OnPropertyChanged(); }
    }

    public int LiveCount => Items.Count(i => !i.Falling);

    private Guid? _copiedId;
    public Guid? CopiedId
    {
        get => _copiedId;
        set { _copiedId = value; OnPropertyChanged(); OnPropertyChanged(nameof(CopiedId)); }
    }

    private Guid? _draggingId;
    public Guid? DraggingId
    {
        get => _draggingId;
        set { _draggingId = value; OnPropertyChanged(); }
    }

    private Guid? _pressedId;
    public Guid? PressedId
    {
        get => _pressedId;
        set { _pressedId = value; OnPropertyChanged(); }
    }

    private bool _revealed;
    public bool Revealed
    {
        get => _revealed;
        set { _revealed = value; OnPropertyChanged(); }
    }

    public static int[] KeepChoices { get; } = { 25, 50, 100 };

    public static int? KeepOnLine
    {
        get
        {
            var n = Settings.Default.GetInt("keepOnLine");
            return KeepChoices.Contains(n) ? n : null;
        }
        set => Settings.Default.SetInt("keepOnLine", value ?? 0);
    }

    public bool SoundOn
    {
        get => Sound.SoundOn;
        set => Sound.SoundOn = value;
    }

    private static readonly string StoreKey = "pegged";

    public Line()
    {
        Items.CollectionChanged += (_, e) => ItemsChanged?.Invoke();
        Restore();
        ScheduleGust();
    }

    // MARK: Hanging and dropping

    public Guid? Hang(string url, bool quietly = false, bool flying = false)
    {
        if (Items.Any(i => i.Url == url && !i.Falling))
        {
            Diag.Write("line", $"hang skipped, already on the line: {Path.GetFileName(url)}");
            return null;
        }
        var thumb = Thumbnail.Make(url, (int)(320 * Layout.SizeScale));
        if (thumb is null)
        {
            if (Media.IsRecording(url))
                thumb = VideoPlaceholder();
            else
            {
                Diag.Write("line", $"hang FAILED, no thumbnail: {Path.GetFileName(url)}");
                return null;
            }
        }
        var item = new PeggedItem(url, thumb) { Flying = flying };
        Items.Add(item);
        Scroll = 0;
        if (LiveCount > MaxItems)
        {
            var oldest = Items.FirstOrDefault(i => !i.Falling);
            if (oldest is not null) LetGo(oldest.Id);
        }
        Save();
        if (!quietly) Sound.PlayHang();
        Diag.Write("line", $"hung {Path.GetFileName(url)} ({thumb.PixelWidth}x{thumb.PixelHeight}), live={LiveCount}");
        return item.Id;
    }

    private static BitmapSource VideoPlaceholder()
    {
        var dv = new DrawingVisual();
        using var dc = dv.RenderOpen();
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 160, 90));
        dc.Close();
        var bmp = new RenderTargetBitmap(160, 90, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    public void Land(Guid id)
    {
        var i = Items.FirstIndex(it => it.Id == id);
        if (i >= 0) Items[i].Flying = false;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, waiting for it if the
    /// caller is somewhere else. Returns false when the caller had to be
    /// marshalled (so the original call site can just return).
    ///
    /// Needed because <see cref="Prune"/> is invoked from
    /// ScreenshotWatcher's timer thread. Dropping an item raises
    /// <see cref="Fall"/>, which the card turns into a WPF animation, and WPF
    /// refuses to start an animation from a non-UI thread - the app died with
    /// an unhandled InvalidOperationException the moment a screenshot was
    /// deleted outside Clothesline.
    /// </summary>
    private static bool RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) return true;
        try
        {
            dispatcher.Invoke(action);
        }
        catch (TaskCanceledException)
        {
            // Dispatcher is shutting down; nothing useful left to do.
        }
        catch (Exception ex)
        {
            // The action itself threw. Invoke rethrows it on this thread, so
            // without this the exception would escape into whichever background
            // caller landed here - the folder watcher's timer callback has no
            // handler of its own, and an unhandled exception on a thread-pool
            // thread terminates the process. Log it and report failure instead.
            Diag.Error("ui", ex);
        }
        return false;
    }

    public void Drop(Guid id, bool quietly = false)
    {
        if (!RunOnUiThread(() => Drop(id, quietly))) return;

        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.Falling) return;
        Diag.Write("line", $"drop {Path.GetFileName(item.Url)}");
        Fall?.Invoke(item);
        item.Falling = true;
        Save();
        if (!quietly) Sound.PlayPop();
        Task.Delay(600).ContinueWith(_ =>
        {
            Application.Current.Dispatcher.Invoke(() =>
                Items.RemoveAll(i => i.Id == id));
        });
    }

    public void Clear()
    {
        var live = Items.Where(i => !i.Falling).ToList();
        TakeDown(live);
    }

    public List<PeggedItem> Neighbours(Guid id, bool toTheLeft)
    {
        var live = Items.Where(i => !i.Falling).ToList();
        var idx = live.FindIndex(i => i.Id == id);
        if (idx < 0) return new();
        return toTheLeft ? live.Take(idx).ToList() : live.Skip(idx + 1).ToList();
    }

    public void TakeDown(Guid id, bool toTheLeft) => TakeDown(Neighbours(id, toTheLeft));

    private void TakeDown(List<PeggedItem> live)
    {
        for (int n = 0; n < live.Count; n++)
        {
            var n1 = n;
            var id = live[n].Id;
            Task.Delay(n1 * 60).ContinueWith(_ =>
                Application.Current.Dispatcher.Invoke(() => Discard(id, quietly: n1 > 0)));
        }
    }

    /// <summary>
    /// Takes down anything whose file has vanished. Called from the folder
    /// watcher, i.e. from a timer thread, so it hops to the UI thread first.
    /// </summary>
    public void Prune()
    {
        if (!RunOnUiThread(Prune)) return;

        foreach (var item in Items.Where(i => !i.Falling).ToList())
        {
            if (!File.Exists(item.Url))
            {
                Diag.Write("line", $"prune: file gone, taking down {Path.GetFileName(item.Url)}");
                Drop(item.Id, quietly: true);
            }
        }
    }

    // MARK: Actions on one photo

    public void Copy(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        try
        {
            var data = new DataObject();
            if (!item.IsRecording)
            {
                var png = Thumbnail.PngData(item.Url);
                if (png is not null) data.SetData("PNG", new MemoryStream(png));
            }
            data.SetFileDropList(new StringCollection { item.Url });
            // Stamp the copy so ClipboardWatcher leaves it alone. The file drop
            // used to do that job, but a Win+Shift+S capture arrives with a
            // file drop of its own, so the marker has to be explicit now.
            data.SetData(ClipboardWatcher.OwnCopyFormat, new MemoryStream(new byte[] { 1 }));
            Clipboard.SetDataObject(data, true);
        }
        catch { Sound.Beep(); return; }

        CopiedId = id;
        Task.Delay(1200).ContinueWith(_ =>
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (CopiedId == id) CopiedId = null;
            }));
    }

    public void Open(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Url) { UseShellExecute = true }); }
        catch { Sound.Beep(); }
    }

    public bool Trash(Guid id, bool quietly = false)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return false;
        try
        {
            // The file can disappear between the menu opening and the click
            // landing - deleted in Explorer, or recycled by another instance.
            // Note that beforehand, or the existence check below reads a file
            // that was already gone as "SHFileOperation removed it".
            bool existedBefore = File.Exists(item.Url);

            var sh = new NativeMethods.SHFILEOPSTRUCT
            {
                wFunc = NativeMethods.FO_DELETE,
                pFrom = item.Url + "\0\0",
                fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT
            };
            int code = NativeMethods.SHFileOperation(ref sh);

            // Do NOT trust the return code. On Windows 11 this deprecated API
            // reports ERROR_FILE_NOT_FOUND (2) even when the file genuinely did
            // reach the Recycle Bin, so code == 0 is not a success signal and
            // code != 0 is not a failure one. The probe in _probe/trashprobe
            // gets 2 for a successful delete and 2 for a failed one alike. The
            // only reliable answer is to look at the filesystem.
            if (File.Exists(item.Url))
            {
                Diag.Write("trash", $"SHFileOperation did not remove the file " +
                                    $"(code {code}, aborted={sh.fAnyOperationsAborted}): " +
                                    $"{Path.GetFileName(item.Url)}");
                if (!quietly) Sound.Beep();
                return false;
            }

            if (!existedBefore)
            {
                // Nothing was recycled, so this is not a success - but the
                // card is still showing a file that is no longer there, so it
                // comes down either way.
                Diag.Write("trash", $"nothing to recycle, already gone: {Path.GetFileName(item.Url)}");
                Drop(id, quietly: true);
                return false;
            }

            Diag.Write("trash", $"recycled {Path.GetFileName(item.Url)} (code {code})");
            if (SoundOn && !quietly) Sound.PlayPop();
            Drop(id, quietly: true);
            return true;
        }
        catch (Exception ex)
        {
            Diag.Fatal("trash", ex);
            if (!quietly) Sound.Beep();
            return false;
        }
    }

    public bool IsInInbox(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return false;
        var inbox = Path.GetFullPath(Inbox.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(item.Url).StartsWith(inbox, StringComparison.OrdinalIgnoreCase);
    }

    public void Discard(Guid id, bool quietly = false)
    {
        bool inbox = IsInInbox(id);
        Diag.Write("line", $"discard -> {(inbox ? "recycle bin" : "plain drop, not in inbox")}");
        if (inbox) Trash(id, quietly: quietly);
        else Drop(id, quietly: quietly);
    }

    public void ScrollBy(double delta)
    {
        var most = Math.Max(0, Items.Count - VisibleCount) * Layout.Spacing;
        var next = Math.Min(most, Math.Max(0, Scroll + delta));
        if (Math.Abs(next - Scroll) > 0.01) Scroll = next;
    }

    public void Trim()
    {
        while (LiveCount > MaxItems)
        {
            var oldest = Items.FirstOrDefault(i => !i.Falling);
            if (oldest is null) break;
            LetGo(oldest.Id);
        }
        Scroll = 0;
    }

    private void LetGo(Guid id)
    {
        if (IsInInbox(id) && Trash(id, quietly: true)) return;
        Diag.Write("line", "let go -> plain drop, recycling did not happen");
        Drop(id, quietly: true);
    }

    public void SaveToDesktop(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var target = UniquePath(desktop, Path.GetFileName(item.Url));
        try
        {
            File.Move(item.Url, target);
            Drop(id, quietly: true);
        }
        catch { Sound.Beep(); }
    }

    private static string UniquePath(string folder, string name)
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = Path.Combine(folder, name);
        var n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{baseName} {n}{ext}");
            n++;
        }
        return candidate;
    }

    public void Markup(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        Diag.Write("line", $"markup enter: id={(item is null ? "gone" : "ok")} exists={(item is not null && File.Exists(item.Url))}");
        if (item is null || !File.Exists(item.Url)) return;
        try
        {
            var win = new AnnotationWindow(item.Url);
            win.ShowDialog();

            var saved = win.SavedPath;
            if (saved is null) return;   // closed without saving

            if (string.Equals(saved, item.Url, StringComparison.OrdinalIgnoreCase))
            {
                // The normal path: the file behind the card was rewritten, so
                // re-read it and tell the card to repaint.
                ReloadThumbnail(item.Url);
            }
            else
            {
                // The user explicitly chose "save as a copy" because the
                // original was locked. That copy is a new picture, so it goes
                // on the line as its own card.
                Hang(saved);
            }
        }
        catch { Sound.Beep(); }
    }

    public void ReloadThumbnail(string url)
    {
        var i = Items.FirstIndex(it => it.Url == url && !it.Falling);
        if (i < 0) return;
        var thumb = Thumbnail.Make(url, (int)(320 * Layout.SizeScale));
        if (thumb is null) return;
        Items[i].Thumb = thumb;
        ThumbChanged?.Invoke(Items[i].Id);
    }

    public void Reveal(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{item.Url}\"") { UseShellExecute = false });
        }
        catch { Sound.Beep(); }
    }

    public void OpenInPhotos(Guid id) => Open(id);

    // MARK: Breeze

    private void ScheduleGust()
    {
        var delay = TimeSpan.FromSeconds(Random.Shared.NextDouble() * 9 + 7);
        Task.Delay(delay).ContinueWith(_ =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Items.Count > 0 && DraggingId is null) Gust++;
                ScheduleGust();
            });
        });
    }

    private int _gust;
    public int Gust
    {
        get => _gust;
        set { _gust = value; OnPropertyChanged(); }
    }

    // MARK: Persistence

    private void Save()
    {
        var paths = Items.Where(i => !i.Falling).Select(i => i.Url).ToList();
        Settings.Default.SetStringList(StoreKey, paths);
    }

    private void Restore()
    {
        var paths = Settings.Default.GetStringList(StoreKey);
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            if (Items.Any(i => i.Url == path)) continue;
            var thumb = Thumbnail.Make(path, (int)(320 * Layout.SizeScale))
                        ?? (Media.IsRecording(path) ? VideoPlaceholder() : null);
            if (thumb is null) continue;
            Items.Add(new PeggedItem(path, thumb));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class ListEx
{
    public static int FirstIndex<T>(this IList<T> list, Func<T, bool> pred)
    {
        for (int i = 0; i < list.Count; i++) if (pred(list[i])) return i;
        return -1;
    }

    public static int RemoveAll<T>(this IList<T> list, Func<T, bool> pred)
    {
        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
            if (pred(list[i])) { list.RemoveAt(i); removed++; }
        return removed;
    }
}
