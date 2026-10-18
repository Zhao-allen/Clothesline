using Microsoft.Win32;

namespace Clothesline.Services;

/// <summary>
/// Which theme the app should paint itself with.
/// </summary>
internal enum ThemeChoice
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
    /// <summary>Raised after the resolved theme changes, so long-lived
    /// surfaces (the tray menu) can repaint without a restart.</summary>
    public static event Action? Changed;

    private static ThemeChoice _mode = ReadMode();
    private static bool _isDark = ComputeIsDark(_mode);
    private static bool _watching;

    public static ThemeChoice Mode
    {
        get => _mode;
        set
        {
            if (value == _mode) return;
            _mode = value;
            Settings.Default.SetString("theme", value switch
            {
                ThemeChoice.Light => "light",
                ThemeChoice.Dark => "dark",
                _ => "system",
            });

            // Re-resolve now: switching System -> Dark (or the reverse) changes
            // what IsDark returns immediately, not on the next system change.
            Apply(ComputeIsDark(_mode));
        }
    }

    public static bool IsDark => _isDark;

    /// <summary>
    /// Starts listening for Windows theme changes. Call once from startup;
    /// without it, "follow the system" would only be re-read on next launch.
    /// </summary>
    public static void WatchSystemChanges()
    {
        if (_watching) return;
        _watching = true;
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // A missing shell (session 0, some remote cases) is not worth
            // failing startup over; the theme simply stays where it is.
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        Apply(ComputeIsDark(_mode));
    }

    /// <summary>
    /// Adopts a resolved theme, telling subscribers only when it actually
    /// moved. The event is what makes a live switch possible.
    /// </summary>
    private static void Apply(bool dark)
    {
        if (dark == _isDark) return;
        _isDark = dark;
        Changed?.Invoke();
    }

    private static ThemeChoice ReadMode() => Settings.Default.GetString("theme") switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.System,
    };

    private static bool ComputeIsDark(ThemeChoice mode) => mode switch
    {
        ThemeChoice.Light => false,
        ThemeChoice.Dark => true,
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
