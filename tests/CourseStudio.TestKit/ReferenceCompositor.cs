using CourseStudio.Core.Models;

namespace CourseStudio.TestKit;

/// <summary>
/// Independent model of what the exported composite MUST contain, derived from the
/// project document and the lesson spec — never from the product's render code.
/// The product under test (FilterGraphBuilder/VideoExporter) is treated as a black
/// box: this class re-derives fit/pad geometry, PIP placement, alpha blending and
/// clip timing from the documented ffmpeg filter semantics and the project model,
/// so a bug in the product cannot reproduce itself in the expectations.
/// </summary>
public sealed class ReferenceCompositor
{
    private readonly CourseProject _project;
    private readonly CourseMediaSet _media;

    public ReferenceCompositor(CourseProject project, CourseMediaSet media)
    {
        _project = project;
        _media = media;
    }

    private enum Content { ScreenA, ScreenB, Camera }

    private Content ContentOf(string sourceId)
    {
        var path = _project.FindSource(sourceId)?.FilePath ?? "";
        if (path == _media.ScreenAPath) return Content.ScreenA;
        if (path == _media.ScreenBPath) return Content.ScreenB;
        if (path == _media.CameraPath) return Content.Camera;
        throw new InvalidOperationException($"Source {sourceId} ({path}) is not part of the lesson media set.");
    }

    private static (int R, int G, int B) Eval(Content content, int frame, int x, int y) => content switch
    {
        Content.ScreenA => LessonSpec.ScreenAContent(frame, x, y),
        Content.ScreenB => LessonSpec.ScreenBContent(frame, x, y),
        _ => LessonSpec.CameraContent(frame, x, y),
    };

    // ---------- geometry (derived from canvas + source sizes, not from graph text) ----------

    /// <summary>Letterbox rectangle of a source fitted onto the canvas (scale-to-fit + centered pad).</summary>
    public static (int X, int Y, int W, int H) FitRect(int sourceWidth, int sourceHeight)
    {
        var scale = Math.Min((double)_canvasW / sourceWidth, (double)_canvasH / sourceHeight);
        var fw = (int)Math.Round(sourceWidth * scale);
        var fh = (int)Math.Round(sourceHeight * scale);
        return ((_canvasW - fw) / 2, (_canvasH - fh) / 2, fw, fh);
    }

    private const int _canvasW = LessonSpec.CanvasWidth;
    private const int _canvasH = LessonSpec.CanvasHeight;

    /// <summary>PIP rectangle implied by the project's PiP settings and the camera aspect.</summary>
    public (int X, int Y, int W, int H) ExpectedPipRect()
    {
        var pw = (int)Math.Round(_canvasW * Math.Clamp(_project.Pip.WidthRatio, 0.05, 1.0));
        // ffmpeg scale=h=-2 rounds the dependent dimension to a multiple of 2.
        var ph = 2 * (int)Math.Round(pw * (double)LessonSpec.CameraHeight / LessonSpec.CameraWidth / 2.0);
        var x = _project.Pip.Corner is PipCorner.TopLeft or PipCorner.BottomLeft
            ? _project.Pip.Margin
            : _canvasW - pw - _project.Pip.Margin;
        var y = _project.Pip.Corner is PipCorner.TopLeft or PipCorner.TopRight
            ? _project.Pip.Margin
            : _canvasH - ph - _project.Pip.Margin;
        return (x, y, pw, ph);
    }

    // ---------- timing ----------

    private (Content content, double sourceSeconds)? ScreenAt(double t)
    {
        foreach (var clip in _project.ScreenClips.OrderBy(c => c.Start.Micros))
        {
            if (t >= clip.Start.TotalSeconds && t < clip.End.TotalSeconds)
                return (ContentOf(clip.SourceId), t - clip.Start.TotalSeconds + clip.SourceIn.TotalSeconds);
        }
        return null;
    }

    /// <summary>Source frame index the screen track must show at timeline time t, or null in a gap.</summary>
    public int? ExpectedScreenSourceFrame(double t)
    {
        var hit = ScreenAt(t);
        return hit is null ? null : (int)Math.Floor(hit.Value.sourceSeconds * LessonSpec.Fps);
    }

    /// <summary>Source frame index the camera overlay must show at t, or null when no camera is visible.</summary>
    public int? ExpectedCameraSourceFrame(double t)
    {
        foreach (var clip in _project.CameraClips.OrderBy(c => c.Start.Micros))
        {
            if (clip.Hidden) continue;
            if (t >= clip.Start.TotalSeconds && t < clip.End.TotalSeconds)
                return (int)Math.Floor((t - clip.Start.TotalSeconds + clip.SourceIn.TotalSeconds) * LessonSpec.Fps);
        }
        return null;
    }

    /// <summary>Timeline positions of the voice beeps: beep k of the source lands at clip.Start + k.</summary>
    public double[] ExpectedBeepOnsets()
    {
        var onsets = new List<double>();
        foreach (var clip in _project.AudioClips.OrderBy(c => c.Start.Micros))
        {
            for (var k = 0; k < LessonSpec.BeepCount; k++)
            {
                var sourceT = (double)k;
                if (sourceT >= clip.SourceIn.TotalSeconds && sourceT < clip.SourceOut.TotalSeconds)
                    onsets.Add(clip.Start.TotalSeconds + (sourceT - clip.SourceIn.TotalSeconds));
            }
        }
        return onsets.ToArray();
    }

    // ---------- pixels ----------

    /// <summary>The exact composite color expected at a canvas pixel at timeline time t.</summary>
    public (int R, int G, int B) ExpectedPixel(int canvasX, int canvasY, double t)
    {
        var screen = ExpectedScreenPixel(canvasX, canvasY, t);
        var camFrame = ExpectedCameraSourceFrame(t);
        if (camFrame is null) return screen;

        var (px, py, pw, ph) = ExpectedPipRect();
        if (canvasX < px || canvasX >= px + pw || canvasY < py || canvasY >= py + ph) return screen;

        var sx = (int)((canvasX - px) * (double)LessonSpec.CameraWidth / pw);
        var sy = (int)((canvasY - py) * (double)LessonSpec.CameraHeight / ph);
        var cam = LessonSpec.CameraContent(camFrame.Value, sx, sy);
        var opacity = Math.Clamp(_project.Pip.Opacity, 0.0, 1.0);
        return ((int)(cam.R * opacity + screen.R * (1 - opacity)),
                (int)(cam.G * opacity + screen.G * (1 - opacity)),
                (int)(cam.B * opacity + screen.B * (1 - opacity)));
    }

    private (int R, int G, int B) ExpectedScreenPixel(int canvasX, int canvasY, double t)
    {
        var hit = ScreenAt(t);
        if (hit is null)
            return ParseHex(_project.BackgroundHex); // canvas background shows in screen gaps

        var (content, sourceSeconds) = hit.Value;
        var frame = (int)Math.Floor(sourceSeconds * LessonSpec.Fps);
        var (sw, sh) = content == Content.ScreenA
            ? (LessonSpec.ScreenAWidth, LessonSpec.ScreenAHeight)
            : (LessonSpec.ScreenBWidth, LessonSpec.ScreenBHeight);
        var (fx, fy, fw, fh) = FitRect(sw, sh);
        if (canvasX < fx || canvasX >= fx + fw || canvasY < fy || canvasY >= fy + fh)
            return ParseHex(ClipBackgroundOf(content));
        var sx = (int)((canvasX - fx) * (double)sw / fw);
        var sy = (int)((canvasY - fy) * (double)sh / fh);
        return Eval(content, frame, sx, sy);
    }

    private string ClipBackgroundOf(Content content) =>
        _project.ScreenClips.First(c => ContentOf(c.SourceId) == content).BackgroundHex;

    // ---------- time-marker strips ----------

    /// <summary>Canvas sample points (16) of the active screen source's frame-index strip at time t.</summary>
    public (int X, int Y)[] ScreenStripSamplePoints(double t)
    {
        var hit = ScreenAt(t) ?? throw new InvalidOperationException($"No screen clip at t={t}.");
        var (sw, sh) = hit.content == Content.ScreenA
            ? (LessonSpec.ScreenAWidth, LessonSpec.ScreenAHeight)
            : (LessonSpec.ScreenBWidth, LessonSpec.ScreenBHeight);
        var (fx, fy, fw, fh) = FitRect(sw, sh);
        var points = new (int X, int Y)[LessonSpec.StripBits];
        for (var bit = 0; bit < LessonSpec.StripBits; bit++)
        {
            var sx = LessonSpec.ScreenStripX + bit * LessonSpec.ScreenStripBarPitch
                     + LessonSpec.ScreenStripBarWidth / 2;
            var sy = LessonSpec.ScreenStripY + LessonSpec.ScreenStripBarHeight / 2;
            points[bit] = (fx + (int)(sx * (double)fw / sw), fy + (int)(sy * (double)fh / sh));
        }
        return points;
    }

    /// <summary>Canvas sample points (16) of the camera overlay's frame-index strip.</summary>
    public (int X, int Y)[] CameraStripSamplePoints()
    {
        var (px, py, pw, ph) = ExpectedPipRect();
        var points = new (int X, int Y)[LessonSpec.StripBits];
        for (var bit = 0; bit < LessonSpec.StripBits; bit++)
        {
            var sx = LessonSpec.CameraStripX + bit * LessonSpec.CameraStripBarPitch
                     + LessonSpec.CameraStripBarWidth / 2;
            var sy = LessonSpec.CameraStripY + LessonSpec.CameraStripBarHeight / 2;
            points[bit] = (px + (int)(sx * (double)pw / LessonSpec.CameraWidth),
                           py + (int)(sy * (double)ph / LessonSpec.CameraHeight));
        }
        return points;
    }

    public static (int R, int G, int B) ParseHex(string hex)
    {
        var h = hex.TrimStart('#');
        return (Convert.ToInt32(h[..2], 16), Convert.ToInt32(h.Substring(2, 2), 16),
            Convert.ToInt32(h.Substring(4, 2), 16));
    }
}
