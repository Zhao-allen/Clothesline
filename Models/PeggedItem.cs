using System.Windows.Media;

namespace Tendedero.Models;

/// <summary>
/// One screenshot hanging on the line. Mirrors the Swift `Pegged` struct.
/// </summary>
internal sealed class PeggedItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Url { get; }
    public ImageSource Thumb { get; set; }
    public double Tilt { get; }

    public bool Falling;
    public bool Flying;

    public bool IsRecording => Media.IsRecording(Url);

    public PeggedItem(string url, ImageSource thumb)
    {
        Url = url;
        Thumb = thumb;
        // Every photo hangs a little crooked, like on a real line.
        Tilt = Random.Shared.NextDouble() * 5.0 - 2.5;
    }
}
