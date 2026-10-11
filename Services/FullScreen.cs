namespace Clothesline.Services;

/// <summary>
/// Detects whether a full-screen app is running on the screen under the
/// pointer, so the line can stay out of the way over videos/presentations.
/// </summary>
internal static class FullScreen
{
    public static bool ShowLineOver
    {
        get => Settings.Default.GetBool("showOverFullScreen");
        set => Settings.Default.SetBool("showOverFullScreen", value);
    }

    /// <summary>True if the line should keep away from the screen under the pointer.</summary>
    public static bool BlocksLineUnderPointer()
    {
        if (ShowLineOver) return false;
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        if (NativeMethods.IsIconic(hwnd)) return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
        if (!NativeMethods.GetCursorPos(out var cursor)) return false;

        // Foreground window covers the cursor's screen?
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = screens.FirstOrDefault(s => s.Bounds.Contains(cursor.X, cursor.Y))
                     ?? System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null) return false;

        var bounds = screen.Bounds;
        // The window must cover essentially the whole screen (no taskbar gap)
        // to be considered full screen.
        return rect.Top <= bounds.Top + 2
               && rect.Left <= bounds.Left + 2
               && rect.Bottom >= bounds.Bottom - 2
               && rect.Right >= bounds.Right - 2;
    }
}
