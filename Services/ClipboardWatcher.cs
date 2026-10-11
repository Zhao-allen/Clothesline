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
        catch { return; }

        _lastCount = count;
        if (data is null) return;

        // Ignore copies that aren't purely an image (text, files, etc.).
        var formats = data.GetFormats();
        if (formats.Contains(DataFormats.Text) || formats.Contains(DataFormats.UnicodeText)
            || formats.Contains(DataFormats.FileDrop) || formats.Contains(DataFormats.Html))
            return;

        byte[]? png = null;
        try
        {
            if (data.GetDataPresent("PNG"))
            {
                if (data.GetData("PNG") is MemoryStream ms)
                    png = ms.ToArray();
            }
            else if (data.GetDataPresent(DataFormats.Bitmap))
            {
                if (data.GetData(DataFormats.Bitmap) is System.Drawing.Bitmap bmp)
                    png = BitmapToPng(bmp);
            }
            else if (data.GetDataPresent(DataFormats.Dib))
            {
                if (data.GetData(DataFormats.Dib) is MemoryStream dib)
                    png = DibToPng(dib);
            }
        }
        catch { return; }

        if (png is null || png.Length == 0 || png.Length > 64 * 1024 * 1024) return;

        if (_last is { } last && last.png.SequenceEqual(png) && File.Exists(last.path)) return;

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
            _onImage(url);
        }
        catch { }
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
