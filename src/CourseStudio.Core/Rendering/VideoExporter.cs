using CourseStudio.Core.Models;

namespace CourseStudio.Core.Rendering;

public sealed record ExportProgress(double Ratio, string Message);

public sealed class ExportOptions
{
    public string VideoCodec { get; init; } = "libx264";
    public string PixelFormat { get; init; } = "yuv420p";
    public string Preset { get; init; } = "medium";
    public int Crf { get; init; } = 18;
    public string AudioCodec { get; init; } = "aac";
    public int AudioBitrateKbps { get; init; } = 192;
}

/// <summary>Runs the real, full-resolution render through ffmpeg.</summary>
public sealed class VideoExporter
{
    private readonly string _ffmpeg;

    public VideoExporter(string? ffmpegPath = null)
    {
        _ffmpeg = ffmpegPath ?? Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG")
            ?? FindOnPath("ffmpeg");
    }

    public async Task ExportAsync(CourseProject project, string outputPath,
        ExportOptions? options = null, IProgress<ExportProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ExportOptions();
        var graph = new FilterGraphBuilder(project).Build();
        if (graph.VideoMapLabel is null)
            throw new InvalidOperationException("Project has no video content to export.");

        var args = new List<string> { "-y", "-hide_banner", "-fflags", "+bitexact" };
        foreach (var input in graph.Inputs)
        {
            args.Add("-i");
            args.Add(input);
        }

        var filterComplex = string.Join(";", graph.FilterLines);
        args.Add("-filter_complex");
        args.Add(filterComplex);
        args.Add("-map");
        args.Add($"[{graph.VideoMapLabel}]");

        if (graph.HasAudio)
        {
            foreach (var label in graph.AudioMapLabels)
            {
                args.Add("-map");
                args.Add($"[{label}]");
            }
            args.Add("-c:a");
            args.Add(options.AudioCodec);
            args.Add("-b:a");
            args.Add($"{options.AudioBitrateKbps}k");
        }
        else
        {
            args.Add("-an");
        }

        args.Add("-c:v");
        args.Add(options.VideoCodec);
        args.Add("-preset");
        args.Add(options.Preset);
        args.Add("-crf");
        args.Add(options.Crf.ToString());
        args.Add("-pix_fmt");
        args.Add(options.PixelFormat);
        args.Add("-r");
        args.Add(project.FrameRate.ToString());
        args.Add("-movflags");
        args.Add("+faststart");
        args.Add(outputPath);

        var psi = new System.Diagnostics.ProcessStartInfo { FileName = _ffmpeg, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        var total = Math.Max(1.0, project.TimelineDuration.TotalSeconds);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var t = ParseTime(e.Data);
            if (t is { } seconds)
                progress?.Report(new ExportProgress(Math.Clamp(seconds / total, 0, 1), e.Data));
        };
        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await using (ct.Register(() => { try { process.Kill(true); } catch { /* exited */ } }))
        {
            await process.WaitForExitAsync(ct);
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}.");
        progress?.Report(new ExportProgress(1, "Export complete."));
    }

    private static double? ParseTime(string ffmpegLine)
    {
        var marker = "time=";
        var idx = ffmpegLine.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var rest = ffmpegLine[(idx + marker.Length)..].TrimStart();
        var end = rest.IndexOf(' ');
        var token = end < 0 ? rest : rest[..end];
        var parts = token.Split(':');
        if (parts.Length != 3 || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var secs)) return null;
        return int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + secs;
    }

    private static string FindOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var ext = OperatingSystem.IsWindows() ? ".exe" : "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, name + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return name + ext;
    }
}
