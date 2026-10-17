using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline.Services;

/// <summary>
/// Polls the clipboard for copied images (PrintScreen or copy in an app).
/// Each found image is saved as a PNG in Clothesline's folder so it hangs like
/// any screenshot. Off by default.
/// </summary>
internal sealed class ClipboardWatcher : IDisposable
{
    private readonly Action<string> _onImage;
    private readonly DispatcherTimer _timer;
    private uint _lastCount;
    private (byte[] png, string path)? _last;
    private bool _disposed;

    public ClipboardWatcher(Action<string> onImage)
    {
        _onImage = onImage;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Check();
    }

    public static bool IsEnabled
    {
        get => Settings.Default.GetBool("clipboardEnabled");
        set => Settings.Default.SetBool("clipboardEnabled", value);
    }

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _lastCount = ClipboardWatcherNative.GetSequenceNumber();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Check()
    {
        if (_disposed) return;
        uint count;
        IDataObject? data;
        try
        {
            count = ClipboardWatcherNative.GetSequenceNumber();
            if (count == _lastCount) return;
            data = Clipboard.GetDataObject();
        }
        catch (Exception ex) { Diag.Error("clip", ex); return; }

        _lastCount = count;
        if (data is null)
        {
            Diag.Write("clip", $"seq {count}: clipboard reported empty");
            return;
        }

        var formats = data.GetFormats();
        var png = TryExtractImage(data, out var why);

        // Every early exit below used to be silent, which made "I pressed
        // Win+Shift+S and nothing happened" impossible to diagnose from the
        // outside. Log the reason and the full format bag instead.
        if (png is null || png.Length == 0 || png.Length > 64 * 1024 * 1024)
        {
            Diag.Write("clip", $"seq {count}: declined ({why}); " +
                               $"{formats.Length} formats = [{string.Join(", ", formats)}]");
            // Naming the managed type behind each format is what turns
            // "declined" into an answer: WeChat's screenshot publishes
            // image/png, and its "Bitmap" resolves to a BitmapSource rather
            // than the System.Drawing.Bitmap the old code demanded.
            if (Diag.Enabled)
                Diag.Write("clip", "  types: " + DescribeFormats(data, formats));
            return;
        }

        if (_last is { } last && last.png.SequenceEqual(png) && File.Exists(last.path))
        {
            Diag.Write("clip", $"seq {count}: duplicate of {Path.GetFileName(last.path)}");
            return;
        }

        Inbox.EnsureFolder();
        var stamp = DateTime.Now.ToString("yyyy-MM-dd 'at' HH.mm.ss");
        var url = Path.Combine(Inbox.Folder, $"Clipboard {stamp}.png");
        var n = 2;
        while (File.Exists(url))
        {
            url = Path.Combine(Inbox.Folder, $"Clipboard {stamp} {n}.png");
            n++;
        }
        try
        {
            File.WriteAllBytes(url, png);
            _last = (png, url);
            Diag.Write("clip", $"seq {count}: saved {png.Length / 1024} KB via {why} -> {Path.GetFileName(url)}");
            _onImage(url);
        }
        catch (Exception ex) { Diag.Error("clip", ex); }
    }

    /// <summary>
    /// Clipboard format Clothesline stamps on its own click-to-copy so the
    /// watcher never hangs a second copy of something already on the line.
    /// The macOS original leaned on the file URL for this, but on Windows the
    /// OS's own screenshot tool also puts a file drop on the clipboard, so a
    /// file drop can no longer be read as "this is not a picture".
    /// </summary>
    public const string OwnCopyFormat = "Clothesline.OwnCopy";

    /// <summary>
    /// Extract a PNG byte array from the clipboard if it currently holds an
    /// image. Returns null otherwise.
    /// Shared between the polling watcher and one-shot callers.
    /// </summary>
    public static byte[]? TryExtractImage(IDataObject? data) => TryExtractImage(data, out _);

    /// <summary>
    /// As <see cref="TryExtractImage(IDataObject?)"/>, but reports why it
    /// declined. Without the reason a filter veto and a clipboard that simply
    /// held no picture look identical from the outside.
    /// </summary>
    public static byte[]? TryExtractImage(IDataObject? data, out string reason)
    {
        reason = "clipboard empty";
        if (data is null) return null;

        var formats = data.GetFormats();

        // Our own click-to-copy. Never hang it a second time.
        if (formats.Contains(OwnCopyFormat)) { reason = "our own copy"; return null; }

        // Text and document copies render themselves as a picture (copying
        // cells out of a spreadsheet, a selection out of a browser), and the
        // original ignores anything riding along with text. A deliberate
        // image copy is a different shape: browsers publish a real PNG
        // stream *together with* the HTML fragment, so the old flat "any
        // text means no" veto rejected every browser copy-image - a copy
        // that should hang, since the original judges clipboard items one
        // by one and the image item survives. Only veto text-carrying
        // clipboards that lack a native PNG stream; that is the
        // document-copy shape, not the copy-image shape.
        // FileDrop is deliberately NOT in this list: the Windows Snipping
        // Tool hands a Win+Shift+S capture over as a temporary PNG that is
        // exposed as a file drop *as well as* an image, so rejecting
        // everything carrying a file drop threw away every single capture.
        if (!formats.Contains("PNG") && !formats.Contains("image/png")
            && (formats.Contains(DataFormats.Text) || formats.Contains(DataFormats.UnicodeText)
                || formats.Contains(DataFormats.Html)))
        {
            reason = "carries text/html and no native PNG stream";
            return null;
        }

        try
        {
            // 1. A real PNG stream. There are two names for the same thing: the
            //    Windows Snipping Tool registers "PNG", while WeChat's
            //    screenshot tool registers the MIME name "image/png". Checking
            //    only "PNG" is why every WeChat capture was silently dropped.
            foreach (var name in PngFormatNames)
            {
                if (!data.GetDataPresent(name)) continue;
                var raw = data.GetData(name);
                if (TryAsBytes(raw, out var bytes))
                {
                    reason = $"native PNG via \"{name}\"";
                    return bytes;
                }
                reason = $"\"{name}\" is {TypeName(raw)}, not bytes";
            }

            // 2. Some other encoded image under a MIME name - WeChat also
            //    publishes image/bmp and image/jpeg. Decode and re-encode.
            foreach (var name in EncodedFormatNames)
            {
                if (!data.GetDataPresent(name)) continue;
                var raw = data.GetData(name);
                if (TryAsBytes(raw, out var bytes))
                {
                    var encoded = BytesToPng(bytes);
                    if (encoded is not null)
                    {
                        reason = $"re-encoded \"{name}\"";
                        return encoded;
                    }
                    reason = $"\"{name}\" could not be decoded";
                }
                else reason = $"\"{name}\" is {TypeName(raw)}, not bytes";
            }

            // 3. "Bitmap". WPF synthesises this format from a
            //    DeviceIndependentBitmap, and what comes back is normally a
            //    BitmapSource rather than the System.Drawing.Bitmap this branch
            //    used to insist on - so it declined and the capture was lost.
            //    WeChat's screenshot hits exactly this path.
            if (data.GetDataPresent(DataFormats.Bitmap))
            {
                var raw = data.GetData(DataFormats.Bitmap);
                switch (raw)
                {
                    case System.Drawing.Bitmap gdi:
                        reason = "GDI+ Bitmap";
                        return BitmapToPng(gdi);
                    case BitmapSource source:
                        reason = "WPF BitmapSource";
                        return SourceToPng(source);
                    case null:
                        reason = "Bitmap is null";
                        break;
                    default:
                        if (TryAsBytes(raw, out var bitmapBytes))
                        {
                            var encoded = BytesToPng(bitmapBytes);
                            if (encoded is not null) { reason = "Bitmap as bytes"; return encoded; }
                        }
                        reason = $"Bitmap is {TypeName(raw)}";
                        break;
                }
            }

            // 4. A raw DIB: BITMAPINFOHEADER + pixels, no file header.
            if (data.GetDataPresent(DataFormats.Dib))
            {
                var raw = data.GetData(DataFormats.Dib);
                switch (raw)
                {
                    case MemoryStream dib:
                        reason = "DIB stream";
                        return DibToPng(dib);
                    case byte[] dibBytes:
                        reason = "DIB bytes";
                        return DibToPng(new MemoryStream(dibBytes));
                    case null:
                        reason = "DIB is null";
                        break;
                    default:
                        reason = $"DIB is {TypeName(raw)}";
                        break;
                }
            }

            // 5. Last resort: let WPF try. Requires the UI thread, which is
            //    where the polling timer runs, so this is normally available.
            if (Application.Current is null || Application.Current.Dispatcher.CheckAccess())
            {
                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                {
                    reason = "Clipboard.GetImage()";
                    return SourceToPng(image);
                }
            }

            reason = "no usable image format";
        }
        catch (Exception ex)
        {
            reason = $"decode threw {ex.GetType().Name}: {ex.Message}";
            return null;
        }

        return null;
    }

    /// <summary>Both spellings of "the clipboard holds a PNG stream".</summary>
    private static readonly string[] PngFormatNames = { "PNG", "image/png" };

    /// <summary>Other encoded image MIME names worth decoding.</summary>
    private static readonly string[] EncodedFormatNames =
        { "image/bmp", "image/jpeg", "image/jpg", "image/webp" };

    private static string TypeName(object? value) => value?.GetType().Name ?? "null";

    /// <summary>
    /// "DeviceIndependentBitmap=MemoryStream, image/png=MemoryStream, ...".
    /// Diagnostic only, and only on the decline path.
    /// </summary>
    private static string DescribeFormats(IDataObject data, string[] formats)
    {
        var parts = new List<string>(formats.Length);
        foreach (var name in formats)
        {
            object? value = null;
            try { value = data.GetData(name); } catch { /* format refuses to convert */ }
            parts.Add($"{name}={TypeName(value)}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>Coaxes a clipboard payload into raw bytes, whatever wrapper it arrived in.</summary>
    private static bool TryAsBytes(object? value, out byte[] bytes)
    {
        switch (value)
        {
            case byte[] direct:
                bytes = direct;
                return direct.Length > 0;
            case MemoryStream ms:
                bytes = ms.ToArray();
                return bytes.Length > 0;
            case Stream stream:
                try
                {
                    if (stream.CanSeek) stream.Position = 0;
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    bytes = copy.ToArray();
                    return bytes.Length > 0;
                }
                catch { bytes = Array.Empty<byte>(); return false; }
            default:
                bytes = Array.Empty<byte>();
                return false;
        }
    }

    private static byte[]? SourceToPng(BitmapSource source)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Decodes an encoded image (BMP/JPEG/WebP) and re-encodes it as PNG.</summary>
    private static byte[]? BytesToPng(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(input,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return SourceToPng(decoder.Frames[0]);
        }
        catch { return null; }
    }

    /// <summary>
    /// Save a PNG byte array into the Inbox folder with a timestamped name,
    /// returning the file path. Returns null on IO failure.
    /// </summary>
    public static string? SavePng(byte[] png)
    {
        if (png.Length == 0 || png.Length > 64 * 1024 * 1024) return null;
        Inbox.EnsureFolder();
        var stamp = DateTime.Now.ToString("yyyy-MM-dd 'at' HH.mm.ss");
        var url = Path.Combine(Inbox.Folder, $"Screenshot {stamp}.png");
        var n = 2;
        while (File.Exists(url))
        {
            url = Path.Combine(Inbox.Folder, $"Screenshot {stamp} {n}.png");
            n++;
        }
        try { File.WriteAllBytes(url, png); return url; }
        catch { return null; }
    }

    private static byte[] BitmapToPng(System.Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] DibToPng(MemoryStream dib)
    {
        // DIB without BITMAPFILEHEADER: prepend one so GDI+ can read it.
        var bytes = dib.ToArray();
        if (bytes.Length < 40) return Array.Empty<byte>();
        var fileHeader = new byte[14];
        fileHeader[0] = (byte)'B';
        fileHeader[1] = (byte)'M';
        var size = BitConverter.GetBytes(14 + bytes.Length);
        Array.Copy(size, 0, fileHeader, 2, 4);
        var offset = 14 + BitConverter.ToInt32(bytes, 10);
        var offBytes = BitConverter.GetBytes(offset);
        Array.Copy(offBytes, 0, fileHeader, 10, 4);
        using var combined = new MemoryStream();
        combined.Write(fileHeader, 0, fileHeader.Length);
        combined.Write(bytes, 0, bytes.Length);
        combined.Position = 0;
        using var bmp = new System.Drawing.Bitmap(combined);
        using var png = new MemoryStream();
        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        return png.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}

internal static class ClipboardWatcherNative
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    public static uint GetSequenceNumber()
    {
        try { return GetClipboardSequenceNumber(); }
        catch { return 0; }
    }
}
