using System.Globalization;
using System.Resources;
using Clothesline.Resources;

namespace Clothesline.Services;

/// <summary>
/// Internationalization helper. Loads localized strings from String.resx
/// satellite assemblies. Falls back to English (the neutral Strings.resx)
/// when a key is missing in the current culture.
/// </summary>
internal static class I18n
{
    /// <summary>
    /// Supported UI cultures, in display order. The neutral English entry
    /// is the fallback and is always first.
    /// </summary>
    public static readonly IReadOnlyList<CultureInfo> Supported = new[]
    {
        CultureInfo.GetCultureInfo("en"),
        CultureInfo.GetCultureInfo("es"),
        CultureInfo.GetCultureInfo("zh"),
        CultureInfo.GetCultureInfo("ja"),
        CultureInfo.GetCultureInfo("fr"),
    };

    private static readonly ResourceManager _manager =
        new ResourceManager(typeof(Strings));

    private static CultureInfo? _override;

    /// <summary>
    /// Effective culture for lookups. Honors an explicit user override,
    /// otherwise follows the system UI culture.
    /// </summary>
    public static CultureInfo Culture
    {
        get => _override ?? CultureInfo.CurrentUICulture;
        set
        {
            _override = value;
            try { CultureInfo.CurrentUICulture = value; } catch { }
            try { CultureInfo.DefaultThreadCurrentUICulture = value; } catch { }
            Changed?.Invoke();
        }
    }

    /// <summary>Raised whenever the active culture changes.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Persisted override culture name (e.g. "zh", "es", "ja", "fr" or "" for
    /// system default). Stored in user settings.
    /// </summary>
    public static string OverrideName
    {
        get => Settings.Default.GetString("language") ?? "";
        set
        {
            Settings.Default.SetString("language", value ?? "");
            Culture = string.IsNullOrWhiteSpace(value)
                ? CultureInfo.CurrentUICulture
                : CultureInfo.GetCultureInfo(value);
        }
    }

    /// <summary>
    /// Loads the saved override on startup, if any. Call once from
    /// <c>App.OnStartup</c> before building any UI.
    /// </summary>
    public static void LoadOverride()
    {
        var name = Settings.Default.GetString("language");
        if (!string.IsNullOrWhiteSpace(name))
        {
            try { _override = CultureInfo.GetCultureInfo(name); } catch { }
        }
    }

    /// <summary>
    /// Looks up a string for the current culture, falling back to English.
    /// </summary>
    public static string Get(string key) =>
        _manager.GetString(key, Culture) ?? _manager.GetString(key, CultureInfo.InvariantCulture) ?? key;

    /// <summary>Formatted lookup; <paramref name="args"/> fill <c>{0}</c> slots.</summary>
    public static string Get(string key, params object[] args)
    {
        var fmt = Get(key);
        try { return string.Format(Culture, fmt, args); }
        catch { return fmt; }
    }
}
