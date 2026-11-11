using System.Text.Json;
using CourseStudio.Core.Models;

namespace CourseStudio.Core.Media;

public sealed record StreamInfo(int Width, int Height, double DurationSeconds, bool HasAudio, bool HasVideo);

/// <summary>Reads a source file's native pixel size and duration via ffprobe.</summary>
public sealed class ProbeService
{
    private readonly string _ffprobe;

    public ProbeService(string? ffprobePath = null)
    {
        _ffprobe = ffprobePath ?? Environment.GetEnvironmentVariable("COURSESTUDIO_FFPROBE")
            ?? FindOnPath("ffprobe");
    }

    public async Task<StreamInfo> ProbeAsync(string filePath, CancellationToken ct = default)
    {
        var args = $"-v error -print_format json -show_format -show_streams \"{filePath}\"";
        var output = await ProcessRunner.RunAsync(_ffprobe, args, ct);
        if (!output.Success)
            throw new InvalidOperationException($"ffprobe failed for {filePath}: {output.StdErr}");

        using var doc = JsonDocument.Parse(output.StdOut);
        var root = doc.RootElement;
        var width = 0;
        var height = 0;
        var hasAudio = false;
        var hasVideo = false;
        double duration = 0;

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = stream.TryGetProperty("codec_type", out var t) ? t.GetString() : "";
                if (codecType == "video")
                {
                    hasVideo = true;
                    width = stream.GetProperty("width").GetInt32();
                    height = stream.GetProperty("height").GetInt32();
                    if (stream.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), out var vd))
                        duration = Math.Max(duration, vd);
                }
                else if (codecType == "audio")
                {
                    hasAudio = true;
                    if (stream.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), out var ad))
                        duration = Math.Max(duration, ad);
                }
            }
        }
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var fd) && double.TryParse(fd.GetString(), out var fmtD))
        {
            duration = Math.Max(duration, fmtD);
        }

        return new StreamInfo(width, height, duration, hasAudio, hasVideo);
    }

    public async Task<MediaSource> ImportAsync(string filePath, MediaKind kind, CancellationToken ct = default)
    {
        var info = await ProbeAsync(filePath, ct);
        return new MediaSource
        {
            Kind = kind,
            FilePath = filePath,
            NaturalWidth = info.Width,
            NaturalHeight = info.Height,
            Duration = TimeCode.FromSeconds(info.DurationSeconds)
        };
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
