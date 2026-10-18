using System.Media;

namespace Clothesline.Services;

/// <summary>
/// Simple sound feedback. Tink when a photo hangs, Pop when it drops.
/// </summary>
internal static class Sound
{
    public static bool SoundOn
    {
        get => !Settings.Default.GetBool("soundOff");
        set => Settings.Default.SetBool("soundOff", !value);
    }

    public static void PlayHang()
    {
        if (!SoundOn) return;
        try { SystemSounds.Asterisk.Play(); } catch { }
    }

    public static void PlayPop()
    {
        if (!SoundOn) return;
        try { SystemSounds.Exclamation.Play(); } catch { }
    }

    public static void Beep()
    {
        try { SystemSounds.Beep.Play(); } catch { }
    }

    /// <summary>Shutter sound when a screenshot is taken via Win+Alt+S.</summary>
    public static void PlayShot()
    {
        if (!SoundOn) return;
        try { SystemSounds.Exclamation.Play(); } catch { }
    }
}
