using System.Globalization;
using CourseStudio.Core.Models;

namespace CourseStudio.Core.Rendering;

public sealed class FilterGraph
{
    public List<string> Inputs { get; } = new();
    public List<string> FilterLines { get; } = new();
    public string? VideoMapLabel { get; set; }
    public List<string> AudioMapLabels { get; } = new();
    public bool HasAudio => AudioMapLabels.Count > 0;
}

/// <summary>
/// Builds one FFmpeg complex filter graph from a project.
/// Sources are referenced by their own input index and always addressed in native
/// pixels; preview downscaling never touches this path, so exports use full source
/// resolution even when the UI preview is small.
/// </summary>
public sealed class FilterGraphBuilder
{
    private readonly CourseProject _project;
    private readonly Dictionary<string, int> _inputIndex = new();
    private readonly List<string> _inputs = new();
    private readonly List<string> _lines = new();
    private int _counter;

    public FilterGraphBuilder(CourseProject project) => _project = project;

    public FilterGraph Build()
    {
        BuildVideo();
        BuildAudio();
        var graph = new FilterGraph
        {
            VideoMapLabel = _videoOut
        };
        graph.Inputs.AddRange(_inputs);
        graph.FilterLines.AddRange(_lines);
        graph.AudioMapLabels.AddRange(_audioOuts);
        return graph;
    }

    private string? _videoOut;
    private readonly List<string> _audioOuts = new();

    private int RegisterInput(string filePath)
    {
        if (_inputIndex.TryGetValue(filePath, out var existing)) return existing;
        var index = _inputs.Count;
        _inputs.Add(filePath);
        _inputIndex[filePath] = index;
        return index;
    }

    private string NextLabel(string prefix) => $"{prefix}{_counter++}";

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string T(TimeCode t) => F(t.TotalSeconds);

    // ---------- video ----------

    private bool BuildVideo()
    {
        var screen = _project.ScreenClips
            .OrderBy(c => c.Start.Micros)
            .ToList();
        if (screen.Count == 0)
        {
            // Camera-only project: camera over canvas background.
            _videoOut = BuildCanvasBase();
            return ApplyCameraOverlays(_videoOut!);
        }

        var pieces = new List<string>();
        foreach (var clip in screen)
        {
            var source = _project.FindSource(clip.SourceId);
            if (source is null) continue;
            var input = RegisterInput(source.FilePath);
            var trimmed = NextLabel("v");
            _lines.Add(
                $"[{input}:v]trim=start={T(clip.SourceIn)}:end={T(clip.SourceOut)}," +
                $"setpts=PTS-STARTPTS,fps={_project.FrameRate},setsar=1[{trimmed}]");

            var fitted = NextLabel("v");
            _lines.Add(BuildFitFilter(trimmed, fitted, source, clip));
            pieces.Add(fitted);
        }

        if (pieces.Count == 1)
        {
            _videoOut = pieces[0];
        }
        else
        {
            var concat = NextLabel("vc");
            _lines.Add($"{string.Concat(pieces.Select(p => $"[{p}]"))}concat=n={pieces.Count}:v=1:a=0[{concat}]");
            _videoOut = concat;
        }

        return ApplyCameraOverlays(_videoOut);
    }

    private string BuildCanvasBase()
    {
        var bg = NextLabel("bg");
        _lines.Add(
            $"color=c={_project.BackgroundHex}:s={_project.CanvasWidth}x{_project.CanvasHeight}:" +
            $"r={_project.FrameRate}:d={T(_project.TimelineDuration)}[{bg}]");
        return bg;
    }

    /// <summary>
    /// Fitting is computed from the source's natural size against the canvas,
    /// never from a cached preview size.
    /// </summary>
    private string BuildFitFilter(string inLabel, string outLabel, MediaSource source, ScreenClip clip)
    {
        var cw = _project.CanvasWidth;
        var ch = _project.CanvasHeight;
        var sw = source.NaturalWidth;
        var sh = source.NaturalHeight;
        var bg = clip.BackgroundHex;

        return clip.ResizeMode switch
        {
            ResizeMode.Fit =>
                $"[{inLabel}]scale=w={cw}:h={ch}:force_original_aspect_ratio=decrease," +
                $"pad={cw}:{ch}:(ow-iw)/2:(oh-ih)/2:color={bg},setsar=1[{outLabel}]",
            ResizeMode.Fill =>
                $"[{inLabel}]scale=w={cw}:h={ch}:force_original_aspect_ratio=increase," +
                $"crop={cw}:{ch},setsar=1[{outLabel}]",
            ResizeMode.Original =>
                $"[{inLabel}]crop=w='min(iw,{cw})':h='min(ih,{ch})'," +
                $"pad={cw}:{ch}:(ow-iw)/2:(oh-ih)/2:color={bg},setsar=1[{outLabel}]",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private bool ApplyCameraOverlays(string baseLabel)
    {
        var current = baseLabel;
        var pipWidth = (int)Math.Round(_project.CanvasWidth * Math.Clamp(_project.Pip.WidthRatio, 0.05, 1.0));

        foreach (var clip in _project.CameraClips.OrderBy(c => c.Start.Micros))
        {
            // Hidden clips do not enter the graph at all; the source file stays on disk and
            // the clip stays on the timeline, so un-hide/undo restores it at the same spot.
            if (clip.Hidden) continue;

            var source = _project.FindSource(clip.SourceId);
            if (source is null) continue;
            var input = RegisterInput(source.FilePath);

            var trimmed = NextLabel("cam");
            _lines.Add(
                $"[{input}:v]trim=start={T(clip.SourceIn)}:end={T(clip.SourceOut)}," +
                $"setpts=PTS-STARTPTS,fps={_project.FrameRate},setsar=1," +
                $"setpts=PTS+{T(clip.Start)}/TB[{trimmed}]");

            var scaled = NextLabel("cam");
            _lines.Add(
                $"[{trimmed}]scale=w={pipWidth}:h=-2,format=yuva420p," +
                $"colorchannelmixer=aa={F(Math.Clamp(_project.Pip.Opacity, 0, 1))}[{scaled}]");

            var (xExpr, yExpr) = OverlayPosition(pipWidth, scaled);
            var overlaid = NextLabel("cam");
            _lines.Add(
                $"[{current}][{scaled}]overlay=x='{xExpr}':y='{yExpr}':" +
                $"enable='gte(t,{T(clip.Start)})*lt(t,{T(clip.End)})'[{overlaid}]");
            current = overlaid;
        }

        _videoOut = current;
        return true;
    }

    private (string x, string y) OverlayPosition(int pipWidth, string scaledLabel)
    {
        var m = _project.Pip.Margin;
        // overlay main_w / main_h refer to the canvas; overlay_w/h to the scaled camera.
        var x = _project.Pip.Corner switch
        {
            PipCorner.TopLeft or PipCorner.BottomLeft => $"{m}",
            _ => $"main_w-overlay_w-{m}"
        };
        var y = _project.Pip.Corner switch
        {
            PipCorner.TopLeft or PipCorner.TopRight => $"{m}",
            _ => $"main_h-overlay_h-{m}"
        };
        return (x, y);
    }

    // ---------- audio ----------

    private void BuildAudio()
    {
        var duration = _project.TimelineDuration;
        if (duration.Micros <= 0 || _project.AudioClips.Count == 0) return;

        var prepared = new List<string>();
        foreach (var clip in _project.AudioClips.OrderBy(c => c.Start.Micros))
        {
            var source = _project.FindSource(clip.SourceId);
            if (source is null) continue;
            var input = RegisterInput(source.FilePath);

            var trimmed = NextLabel("a");
            _lines.Add(
                $"[{input}:a]atrim=start={T(clip.SourceIn)}:end={T(clip.SourceOut)}," +
                $"asetpts=PTS-STARTPTS,volume={F(clip.Gain)}[{trimmed}]");

            // Independent origins: delay positions the clip on the timeline without
            // padding silence into it; mixing and the final trim handle the total length.
            var delayed = NextLabel("a");
            _lines.Add(
                $"[{trimmed}]adelay=delays={clip.Start.Micros / 1000}:all=1[{delayed}]");
            prepared.Add(delayed);
        }

        if (prepared.Count == 0) return;

        var mixed = NextLabel("mix");
        if (prepared.Count == 1)
        {
            _lines.Add($"[{prepared[0]}]atrim=duration={T(duration)},asetpts=PTS-STARTPTS[{mixed}]");
        }
        else
        {
            var inputs = string.Concat(prepared.Select(p => $"[{p}]"));
            _lines.Add(
                $"{inputs}amix=inputs={prepared.Count}:duration=longest:normalize=0," +
                $"atrim=duration={T(duration)},asetpts=PTS-STARTPTS[{mixed}]");
        }

        var muted = ApplyMuteRegions(mixed);
        _audioOuts.Add(muted);
    }

    /// <summary>Mute regions drive a volume envelope only; no samples are removed and nothing shifts.</summary>
    private string ApplyMuteRegions(string inLabel)
    {
        if (_project.MuteRegions.Count == 0) return inLabel;

        // Nested if(between(...),0,<inner>) so any number of regions share one envelope.
        var expr = "1";
        foreach (var region in _project.MuteRegions.OrderBy(r => r.Start.Micros))
        {
            expr = $"if(between(t\\,{T(region.Start)}\\,{T(region.End)})\\,0\\,{expr})";
        }
        var outLabel = NextLabel("am");
        _lines.Add($"[{inLabel}]volume='{expr}'[{outLabel}]");
        return outLabel;
    }
}
