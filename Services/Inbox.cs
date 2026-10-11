using System.IO;

namespace Tendedero.Services;

/// <summary>
/// Inbox mode: where screenshots are kept. Unlike macOS, Windows has no system
/// API to redirect the Win+PrintScreen save location, so Inbox mode here means
/// watching the user's Screenshots folder and (optionally) moving captures into
/// Tendedero's own folder so the Desktop/Pictures folder stays clear.
/// </summary>
internal static class Inbox
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tendedero", "Screenshots");

    public static string DesktopFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>
    /// The folder Windows saves Win+PrintScreen screenshots to by default.
    /// </summary>
    public static string ScreenshotsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public static bool IsEnabled
    {
        get => Settings.Default.GetBool("inboxEnabled");
        set => Settings.Default.SetBool("inboxEnabled", value);
    }

    public static bool WasOffered
    {
        get => Settings.Default.GetBool("inboxOffered");
        set => Settings.Default.SetBool("inboxOffered", value);
    }

    public static void EnsureFolder()
    {
        try { Directory.CreateDirectory(Folder); } catch { }
    }
}
