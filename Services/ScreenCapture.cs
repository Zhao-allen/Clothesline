using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Clothesline.Services;

/// <summary>
/// Captures the screen using BitBlt and saves a PNG to the Screenshots folder.
/// This gives Clothesline its own screenshot shortcut (Win+Alt+S) so users
/// don't have to rely on Win+PrintScreen.
/// </summary>
internal static class ScreenCapture
{
    /// <summary>
    /// Capture the monitor under the pointer and save a timestamped PNG to the
    /// primary screenshots folder. Returns the saved file path, or null on
    /// failure.
    /// </summary>
    public static string? CaptureScreenUnderPointer()
    {
        try
        {
            var screen = LineWindow.ScreenUnderPointer();
            if (screen is null) return null;
            var bounds = screen.Bounds;
            return CaptureRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Capture the entire virtual screen (all monitors).</summary>
    public static string? CaptureAllScreens()
    {
        try
        {
            int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            return CaptureRegion(x, y, w, h);
        }
        catch
        {
            return null;
        }
    }

    private static string? CaptureRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        IntPtr hdcScreen = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
            hBitmap = NativeMethods.CreateCompatibleBitmap(hdcScreen, width, height);
            hOld = NativeMethods.SelectObject(hdcMem, hBitmap);

            NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, NativeMethods.SRCCOPY);

            using var bmp = Image.FromHbitmap(hBitmap);
            string folder = Inbox.IsEnabled ? Inbox.Folder : Inbox.ScreenshotsFolder;
            Directory.CreateDirectory(folder);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string path = Path.Combine(folder, $"Clothesline-{stamp}.png");
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                NativeMethods.SelectObject(hdcMem, hOld);
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero) NativeMethods.DeleteDC(hdcMem);
            if (hdcScreen != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }
}
