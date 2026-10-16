using System.IO;
using System.Text;

namespace Clothesline.Services;

/// <summary>
/// Append-only diagnostic log written to
/// <c>%APPDATA%\Clothesline\diag.log</c>.
///
/// Clothesline is full of silent failure paths - a capture the clipboard
/// filter rejects, a menu row whose action throws, a watcher timer that fires
/// on the wrong thread - and none of them leave a trace anywhere the user can
/// see. That turns "I pressed the key and nothing happened" into a guessing
/// game. One line per interesting event turns it into "the clipboard carried
/// these 11 formats and the filter stopped at step 3".
///
/// Rules: never throw, never block the caller, never grow without bound.
/// </summary>
internal static class Diag
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static int _failed;

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Clothesline");

    public static string LogPath => Path.Combine(Folder, "diag.log");

    /// <summary>
    /// On by default so a user can just send us the file. Set
    /// <c>CLOTHESLINE_DIAG=0</c> to silence it.
    /// </summary>
    public static bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable("CLOTHESLINE_DIAG"), "0",
                       StringComparison.Ordinal);

    /// <summary>One line per event. Area is a short tag, e.g. "tray", "clip".</summary>
    public static void Write(string area, string message)
    {
        if (!Enabled) return;
        // Give up only after repeated failures, so one locked file at
        // shutdown does not blind the rest of the session.
        if (Volatile.Read(ref _failed) > 5) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                Roll();
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{area,-8}] {message}{Environment.NewLine}",
                    Utf8);
            }
        }
        catch
        {
            Interlocked.Increment(ref _failed);
        }
    }

    /// <summary>Logs an exception in a form that survives a bug report.</summary>
    public static void Error(string area, Exception ex)
        => Write(area, $"!! {ex.GetType().Name}: {ex.Message} @ {ex.TargetSite?.Name}");

    /// <summary>
    /// Full stack trace, one frame per line. Reserved for the cases where the
    /// call path is the whole point - a crash, or a menu row that threw.
    /// </summary>
    public static void Fatal(string area, Exception ex)
    {
        var lines = ex.ToString().Split('\n');
        Write(area, "!! " + lines[0].TrimEnd('\r'));
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            Write(area, "   " + line);
        }
    }

    /// <summary>Horizontal rule + banner, so sessions are easy to tell apart.</summary>
    public static void Session(string label)
    {
        Write("app", new string('-', 46));
        Write("app", $"session start - {label} - pid {Environment.ProcessId}");
    }

    /// <summary>Keeps the file from growing forever: rolls to diag.1.log at 1 MB.</summary>
    private static void Roll()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || info.Length < MaxBytes) return;
            var old = Path.Combine(Folder, "diag.1.log");
            File.Delete(old);
            File.Move(LogPath, old);
        }
        catch { }
    }
}
