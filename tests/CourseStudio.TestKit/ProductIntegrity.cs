using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CourseStudio.TestKit;

public sealed record IntegrityReport(
    bool FileExists,
    long FileSize,
    bool Probed,
    int Width,
    int Height,
    bool HasAudio,
    bool HasVideo,
    double DurationSeconds,
    int VideoFrameCount,
    bool MoovBeforeMdat,
    string? Error)
{
    public bool IsCompleteLesson(int expectedWidth, int expectedHeight, double expectedDuration,
        int expectedFrames, double durationTolerance) =>
        FileExists && Probed && HasVideo && HasAudio &&
        Width == expectedWidth && Height == expectedHeight &&
        Math.Abs(DurationSeconds - expectedDuration) <= durationTolerance &&
        VideoFrameCount == expectedFrames &&
        MoovBeforeMdat;
}

/// <summary>
/// Inspects an exported file as an external consumer would: container atoms,
/// ffprobe stream metadata and a full decode of the video track. Used both to
/// prove the product is complete and to prove a cancelled export is NOT.
/// </summary>
public static class ProductIntegrity
{
    public static IntegrityReport Inspect(string path, string? ffmpeg = null, string? ffprobe = null)
    {
        ffmpeg ??= FfmpegTools.Ffmpeg;
        ffprobe ??= FfmpegTools.Ffprobe;

        if (!File.Exists(path))
            return new IntegrityReport(false, 0, false, 0, 0, false, false, 0, 0, false, "missing");

        var size = new FileInfo(path).Length;
        try
        {
            var (width, height, hasAudio, hasVideo, duration) = Probe(path, ffprobe);
            var frames = CountVideoFrames(path, ffmpeg);
            var moovFirst = MoovPrecedesMdat(path);
            return new IntegrityReport(true, size, true, width, height, hasAudio, hasVideo,
                duration, frames, moovFirst, null);
        }
        catch (Exception ex)
        {
            return new IntegrityReport(true, size, false, 0, 0, false, false, 0, 0, false, ex.Message);
        }
    }

    private static (int Width, int Height, bool HasAudio, bool HasVideo, double Duration) Probe(
        string path, string ffprobe)
    {
        var output = Run(ffprobe,
            $"-v error -print_format json -show_format -show_streams \"{path}\"");
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        var width = 0;
        var height = 0;
        var hasAudio = false;
        var hasVideo = false;
        double duration = 0;
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var type = stream.GetProperty("codec_type").GetString();
            if (type == "video")
            {
                hasVideo = true;
                width = stream.GetProperty("width").GetInt32();
                height = stream.GetProperty("height").GetInt32();
            }
            else if (type == "audio")
            {
                hasAudio = true;
            }
        }
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var d))
            double.TryParse(d.GetString(), out duration);
        return (width, height, hasAudio, hasVideo, duration);
    }

    private static int CountVideoFrames(string path, string ffmpeg)
    {
        var stderr = Run(ffmpeg, $"-hide_banner -i \"{path}\" -map 0:v:0 -f null -", expectStdErr: true);
        var matches = Regex.Matches(stderr, @"frame=\s*(\d+)");
        if (matches.Count == 0)
            throw new InvalidOperationException("Could not decode any video frame.");
        return int.Parse(matches[^1].Groups[1].Value);
    }

    /// <summary>Walks the top-level MP4 atoms; faststart files carry moov before mdat.</summary>
    private static bool MoovPrecedesMdat(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[16];
        long moov = -1, mdat = -1;
        while (stream.Position < stream.Length)
        {
            var here = stream.Position;
            if (stream.Read(header, 0, 8) < 8) break;
            long size = ((long)header[0] << 24) | ((long)header[1] << 16) | ((long)header[2] << 8) | header[3];
            var type = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8;
            if (size == 1)
            {
                if (stream.Read(header, 8, 8) < 8) break;
                size = ((long)header[8] << 56) | ((long)header[9] << 48) | ((long)header[10] << 40) |
                       ((long)header[11] << 32) | ((long)header[12] << 24) | ((long)header[13] << 16) |
                       ((long)header[14] << 8) | header[15];
                headerSize = 16;
            }
            if (type == "moov") moov = here;
            if (type == "mdat") { mdat = here; break; }
            if (size <= 0) break;
            stream.Seek(here + size, SeekOrigin.Begin);
            _ = headerSize;
        }
        return moov >= 0 && (mdat < 0 || moov < mdat);
    }

    private static string Run(string exe, string arguments, bool expectStdErr = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in CourseStudio.Core.Media.ProcessRunner.SplitArguments(arguments))
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{exe} exited {process.ExitCode}: {stderr.GetAwaiter().GetResult()}");
        return expectStdErr ? stderr.GetAwaiter().GetResult() : stdout.GetAwaiter().GetResult();
    }
}
