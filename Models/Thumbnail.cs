using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clothesline.Models;

internal static class Media
{
    private static readonly HashSet<string> RecordingExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v"
    };

    public static bool IsRecording(string path) =>
        RecordingExtensions.Contains(Path.GetExtension(path));
}

/// <summary>
/// Generates a thumbnail BitmapSource for an image file, capped at maxPixels.
/// Videos return null (caller draws a placeholder).
/// </summary>
internal static class Thumbnail
{
    public static BitmapSource? Make(string path, int maxPixels)
    {
        if (Media.IsRecording(path)) return null;
        try
        {
            // Read through a stream we own and close it again, then copy the
            // pixels into a WriteableBitmap.
            //
            // Two reasons this is not the obvious "decode the Uri" version:
            //  * A TransformedBitmap is lazy. Wrapped around a decoder opened
            //    with DelayCreation it keeps that decoder - and therefore the
            //    file - alive for as long as the card is on the line. That is
            //    what stopped the annotation window from replacing the original
            //    and pushed it onto the "<name>-annotated.png" fallback.
            //  * Decoding a Uri goes through WPF's decoder cache, so re-reading
            //    a file we just overwrote could hand back the stale image.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            double scale = Math.Min(1.0, (double)maxPixels / Math.Max(frame.PixelWidth, frame.PixelHeight));
            BitmapSource scaled = scale >= 1.0
                ? frame
                : new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            var eager = new WriteableBitmap(scaled);
            eager.Freeze();
            return eager;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads raw PNG bytes from an image file path (converting if needed).</summary>
    public static byte[]? PngData(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllBytes(path);
            var decoder = BitmapDecoder.Create(new Uri(path),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(decoder.Frames[0]);
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
