namespace CourseStudio.Core.Models;

/// <summary>
/// One placed piece of a media source on a timeline track.
/// SourceIn/SourceOut address the source file in its own native timeline;
/// Start is the clip position on the project timeline. Clips never touch
/// the source file, including when the camera is hidden.
/// </summary>
public abstract class Clip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceId { get; set; } = string.Empty;
    public TimeCode Start { get; set; }
    public TimeCode SourceIn { get; set; }
    public TimeCode SourceOut { get; set; }
    public abstract TrackKind Track { get; }

    public TimeCode Duration => new(SourceOut.Micros - SourceIn.Micros);
    public TimeCode End => new(Start.Micros + Duration.Micros);

    public abstract Clip Clone();

    protected void CopyTo(Clip other)
    {
        // A clone produced by a split (ripple delete) is a distinct timeline object:
        // give it a fresh id while the source mapping stays explicit in the caller.
        other.Id = Guid.NewGuid().ToString("N");
        other.SourceId = SourceId;
        other.Start = Start;
        other.SourceIn = SourceIn;
        other.SourceOut = SourceOut;
    }
}

public sealed class ScreenClip : Clip
{
    public override TrackKind Track => TrackKind.Screen;
    public ResizeMode ResizeMode { get; set; } = ResizeMode.Fit;
    public string BackgroundHex { get; set; } = "#000000";

    public override Clip Clone()
    {
        var c = new ScreenClip { ResizeMode = ResizeMode, BackgroundHex = BackgroundHex };
        CopyTo(c);
        return c;
    }
}

public sealed class CameraClip : Clip
{
    public override TrackKind Track => TrackKind.Camera;

    /// <summary>
    /// Hidden only changes compositing: the clip stays on the timeline and the
    /// source file is never deleted or trimmed. Un-hiding restores the PIP.
    /// </summary>
    public bool Hidden { get; set; }

    public override Clip Clone()
    {
        var c = new CameraClip { Hidden = Hidden };
        CopyTo(c);
        return c;
    }
}

public sealed class AudioClip : Clip
{
    public override TrackKind Track => TrackKind.Audio;
    public double Gain { get; set; } = 1.0;

    public override Clip Clone()
    {
        var c = new AudioClip { Gain = Gain };
        CopyTo(c);
        return c;
    }
}
