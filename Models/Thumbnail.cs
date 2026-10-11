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
            var decoder = BitmapDecoder.Create(
                new Uri(path),
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            double scale = Math.Min(1.0, (double)maxPixels / Math.Max(frame.PixelWidth, frame.PixelHeight));
            if (scale >= 1.0)
            {
                var bmp = new WriteableBitmap(frame);
                bmp.Freeze();
                return bmp;
            }
            var transformed = new TransformedBitmap(frame,
                new ScaleTransform(scale, scale));
            transformed.Freeze();
            return transformed;
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
