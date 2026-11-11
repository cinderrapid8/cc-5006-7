using CourseStudio.Core.Editing;
using CourseStudio.Core.Models;
using CourseStudio.Core.Rendering;
using Xunit;

namespace CourseStudio.Tests;

/// <summary>
/// End-to-end render tests using a real ffmpeg binary (COURSESTUDIO_FFMPEG / PATH).
/// They verify the produced file, not just the graph text.
/// </summary>
public class EndToEndRenderTests
{
    private static string Ffmpeg =>
        Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG") ?? "ffmpeg";

    private static bool FfmpegAvailable()
    {
        try
        {
            var output = Core.Media.ProcessRunner.RunAsync(Ffmpeg, "-version").GetAwaiter().GetResult();
            return output.Success;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task Export_ProducesPlayableMp4_WithMuteAndHiddenCamera()
    {
        if (!FfmpegAvailable()) return; // environment without ffmpeg: skip silently
        var dir = Path.Combine(Path.GetTempPath(), "coursestudio-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var screen = Path.Combine(dir, "screen.mp4");
        var cam = Path.Combine(dir, "cam.mp4");
        var voice = Path.Combine(dir, "voice.wav");

        await Run($"-y -f lavfi -i testsrc=size=1920x1080:rate=30 -t 6 -pix_fmt yuv420p \"{screen}\"");
        await Run($"-y -f lavfi -i color=c=purple:size=640x480:rate=30 -t 12 -pix_fmt yuv420p \"{cam}\"");
        await Run($"-y -f lavfi -i sine=frequency=440:duration=10 \"{voice}\"");

        var probe = new Core.Media.ProbeService(
            Environment.GetEnvironmentVariable("COURSESTUDIO_FFPROBE") ?? "ffprobe");
        var p = new CourseProject { CanvasWidth = 1280, CanvasHeight = 720, FrameRate = 30 };
        var sSource = await probe.ImportAsync(screen, MediaKind.Screen);
        var cSource = await probe.ImportAsync(cam, MediaKind.Camera);
        var aSource = await probe.ImportAsync(voice, MediaKind.Audio);
        p.Sources.AddRange(new[] { sSource, cSource, aSource });

        p.ScreenClips.Add(new ScreenClip
        {
            SourceId = sSource.Id, Start = TimeCode.Zero,
            SourceIn = TimeCode.Zero, SourceOut = sSource.Duration
        });
        p.CameraClips.Add(new CameraClip
        {
            SourceId = cSource.Id, Start = TimeCode.FromSeconds(1),
            SourceIn = TimeCode.Zero, SourceOut = TimeCode.FromSeconds(8)
        });
        p.AudioClips.Add(new AudioClip
        {
            SourceId = aSource.Id, Start = TimeCode.FromSeconds(0.5),
            SourceIn = TimeCode.Zero, SourceOut = TimeCode.FromSeconds(9)
        });
        p.MuteRegions.Add(new MuteRegion
        {
            Start = TimeCode.FromSeconds(2),
            End = TimeCode.FromSeconds(3)
        });
        p.CameraClips[0].Hidden = true;

        var output = Path.Combine(dir, "out.mp4");
        var exporter = new VideoExporter(Ffmpeg);
        await exporter.ExportAsync(p, output, new ExportOptions { Preset = "ultrafast" });

        Assert.True(File.Exists(output));
        var info = new FileInfo(output);
        Assert.True(info.Length > 1000);

        var outputInfo = await probe.ProbeAsync(output);
        Assert.Equal(1280, outputInfo.Width);
        Assert.Equal(720, outputInfo.Height);
        Assert.True(outputInfo.HasVideo);
        Assert.True(outputInfo.HasAudio);
        Assert.True(outputInfo.DurationSeconds >= 5.5);
    }

    private static async Task Run(string args)
    {
        var result = await Core.Media.ProcessRunner.RunAsync(Ffmpeg, args);
        Assert.True(result.Success, $"ffmpeg failed: {result.StdErr}");
    }
}
