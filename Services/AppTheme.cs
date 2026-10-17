using Microsoft.Win32;

namespace Clothesline.Services;

/// <summary>
/// Which theme the app should paint itself with.
/// </summary>
internal enum ThemeMode
{
    /// <summary>Follow the Windows app theme (Settings > Personalization > Colors).</summary>
    System = 0,

    /// <summary>Always light.</summary>
    Light = 1,

    /// <summary>Always dark.</summary>
    Dark = 2,
}

/// <summary>
/// The app's theme choice: what the user picked, and what that resolves to
/// right now.
///
/// The mode is persisted under the "theme" key of settings.json and holds one
/// of "system" / "light" / "dark". Anything unrecognised falls back to
/// System, so a hand-edited or older settings file still works.
///
/// <see cref="IsDark"/> is the value surfaces actually paint from. Under
/// System it reads the same registry value the installer does
/// (AppsUseLightTheme), so the app and its installer agree on what "follow
/// the system" means.
/// </summary>
internal static class AppTheme
{
    public static ThemeMode Mode
    {
        get => Settings.Default.GetString("theme") switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        set
        {
            Settings.Default.SetString("theme", value switch
            {
                ThemeMode.Light => "light",
                ThemeMode.Dark => "dark",
                _ => "system",
            });
        }
    }

    public static bool IsDark => Mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.Dark => true,
        _ => SystemUsesDark(),
    };

    /// <summary>
    /// Reads the Windows app theme. AppsUseLightTheme is 1 for light and 0
    /// for dark; it lives under HKCU so no elevation is needed.
    ///
    /// Defaults to dark when the key is missing or unreadable - the same
    /// fallback Setup/InstallerWPF uses, so an app that cannot tell still
    /// matches its installer.
    /// </summary>
    private static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int i) return i == 0;
        }
        catch
        {
            // A locked-down or remote registry should not change the theme.
        }

        return true;
    }
}
