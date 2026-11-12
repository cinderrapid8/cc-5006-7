using System.Diagnostics;

namespace CourseStudio.TestKit;

/// <summary>Paths of the generated synthetic lesson sources.</summary>
public sealed record CourseMediaSet(
    string Directory,
    string ScreenAPath,
    string ScreenBPath,
    string CameraPath,
    string VoicePath);

/// <summary>
/// Renders the synthetic lesson sources defined by <see cref="LessonSpec"/> through
/// ffmpeg (rawvideo pipe) and a PCM wave writer. Regenerated per acceptance run so
/// the suite never depends on checked-in binaries.
/// </summary>
public static class SyntheticCourseMedia
{
    public static CourseMediaSet Generate(string directory, string? ffmpeg = null)
    {
        ffmpeg ??= FfmpegTools.Ffmpeg;
        System.IO.Directory.CreateDirectory(directory);
        var set = new CourseMediaSet(
            directory,
            Path.Combine(directory, "screen-a.mp4"),
            Path.Combine(directory, "screen-b.mp4"),
            Path.Combine(directory, "camera.mp4"),
            Path.Combine(directory, "voice.wav"));

        RenderVideo(set.ScreenAPath, LessonSpec.ScreenAWidth, LessonSpec.ScreenAHeight,
            LessonSpec.ScreenAFrames, LessonSpec.ScreenAContent, ffmpeg);
        RenderVideo(set.ScreenBPath, LessonSpec.ScreenBWidth, LessonSpec.ScreenBHeight,
            LessonSpec.ScreenBFrames, LessonSpec.ScreenBContent, ffmpeg);
        RenderVideo(set.CameraPath, LessonSpec.CameraWidth, LessonSpec.CameraHeight,
            LessonSpec.CameraFrames, LessonSpec.CameraContent, ffmpeg);
        WriteVoice(set.VoicePath);
        return set;
    }

    private static void RenderVideo(string path, int width, int height, int frames,
        Func<int, int, int, (int R, int G, int B)> content, string ffmpeg)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[]
                 {
                     "-y", "-hide_banner", "-loglevel", "error",
                     "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", $"{width}x{height}",
                     "-r", LessonSpec.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-i", "-",
                     "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10",
                     "-pix_fmt", "yuv420p", "-movflags", "+faststart", path
                 })
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var frame = new RgbFrame(width, height);
        var px = frame.Raw;
        for (var f = 0; f < frames; f++)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width * 3;
                for (var x = 0; x < width; x++)
                {
                    var (r, g, b) = content(f, x, y);
                    var o = row + x * 3;
                    px[o] = (byte)r;
                    px[o + 1] = (byte)g;
                    px[o + 2] = (byte)b;
                }
            }
            process.StandardInput.BaseStream.Write(px, 0, px.Length);
        }
        process.StandardInput.Close();
        process.WaitForExit();
        var err = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed rendering {path}: {err}");
    }

    private static void WriteVoice(string path)
    {
        var totalSamples = (long)LessonSpec.BeepCount * LessonSpec.VoiceSampleRate;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        var dataBytes = (int)(totalSamples * 2);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);          // PCM chunk size
        writer.Write((short)1);    // PCM
        writer.Write((short)1);    // mono
        writer.Write(LessonSpec.VoiceSampleRate);
        writer.Write(LessonSpec.VoiceSampleRate * 2); // byte rate
        writer.Write((short)2);    // block align
        writer.Write((short)16);   // bits per sample
        writer.Write("data"u8);
        writer.Write(dataBytes);
        var buffer = new byte[LessonSpec.VoiceSampleRate * 2]; // one second per flush
        for (long s = 0; s < totalSamples; s++)
        {
            var offset = (int)(s % LessonSpec.VoiceSampleRate) * 2;
            var sample = LessonSpec.VoiceSample(s);
            buffer[offset] = (byte)(sample & 0xFF);
            buffer[offset + 1] = (byte)(sample >> 8);
            if (offset == buffer.Length - 2) writer.Write(buffer);
        }
    }
}
