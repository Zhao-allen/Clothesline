using System.Media;

namespace Tendedero.Services;

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
}
