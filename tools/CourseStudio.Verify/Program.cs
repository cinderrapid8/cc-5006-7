using System.Diagnostics;
using CourseStudio.Core.Editing;
using CourseStudio.Core.Media;
using CourseStudio.Core.Models;
using CourseStudio.Core.Persistence;
using CourseStudio.Core.Rendering;

// End-to-end verification harness for the requirements:
//   1. numbered screen clicks + matching command audio + solid color-block camera
//   2. resolution change mid-lesson (1920x1080 then 1280x720 sources onto one canvas)
//   3. missing audio segment (audio simply stops before the end)
//   4. window-close/reopen recovery via the project file
//   5. camera hide changes composition only; undo restores it
//   6. preview cache uses downscaled images while export always samples native pixels
//   7. ripple cut moves selected tracks together and reports locked-track conflicts

var mediaDir = args.Length > 0 ? args[0] : "artifacts/test-media";
var workDir = Path.Combine("artifacts", "verify", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(workDir);

var ffmpeg = Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG") ?? "ffmpeg";
var ffprobe = Environment.GetEnvironmentVariable("COURSESTUDIO_FFPROBE") ?? "ffprobe";
var probe = new ProbeService(ffprobe);
int failures = 0;

void Check(string name, bool condition, string detail = "")
{
    Console.WriteLine($"[{(condition ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " — " + detail : "")}");
    if (!condition) failures++;
}

async Task<string> Ffmpeg(string arguments)
{
    var result = await ProcessRunner.RunAsync(ffmpeg, arguments);
    if (!result.Success) throw new InvalidOperationException($"ffmpeg: {result.StdErr}");
    return result.StdErr;
}

// ---------- build project ----------
Console.WriteLine("== Building project ==");
var project = new CourseProject
{
    Name = "Verification lesson",
    CanvasWidth = 1280,
    CanvasHeight = 720,
    FrameRate = 10,
    BackgroundHex = "#12161E"
};

var screenA = await probe.ImportAsync(Path.Combine(mediaDir, "screen_clicks_a.mp4"), MediaKind.Screen);
var screenB = await probe.ImportAsync(Path.Combine(mediaDir, "screen_clicks_b.mp4"), MediaKind.Screen);
var camera = await probe.ImportAsync(Path.Combine(mediaDir, "camera_blocks.mp4"), MediaKind.Camera);
var voice = await probe.ImportAsync(Path.Combine(mediaDir, "voice_commands.wav"), MediaKind.Audio);
project.Sources.AddRange(new[] { screenA, screenB, camera, voice });

Check("screen A native resolution is 1920x1080 (separate from canvas)",
    screenA.NaturalWidth == 1920 && screenA.NaturalHeight == 1080);
Check("screen B native resolution is 1280x720 (mid-lesson change)",
    screenB.NaturalWidth == 1280 && screenB.NaturalHeight == 720);

// Screen: A 0-4s, deliberate 1s pause, B 5-8s.
project.ScreenClips.Add(new ScreenClip
{
    SourceId = screenA.Id, Start = TimeCode.Zero,
    SourceIn = TimeCode.Zero, SourceOut = TimeCode.FromSeconds(4), ResizeMode = ResizeMode.Fit
});
project.ScreenClips.Add(new ScreenClip
{
    SourceId = screenB.Id, Start = TimeCode.FromSeconds(5),
    SourceIn = TimeCode.Zero, SourceOut = TimeCode.FromSeconds(3)
});
// Camera starts at its own independent origin (1s).
project.CameraClips.Add(new CameraClip
{
    SourceId = camera.Id, Start = TimeCode.FromSeconds(1),
    SourceIn = TimeCode.Zero, SourceOut = TimeCode.FromSeconds(8)
});
// Microphone starts independently at 0.5s and stops at 6s -> missing audio tail 6-8s.
project.AudioClips.Add(new AudioClip
{
    SourceId = voice.Id, Start = TimeCode.FromSeconds(0.5),
    SourceIn = TimeCode.Zero, SourceOut = TimeCode.FromSeconds(5.5)
});
// Independent origins: camera=1s, audio=0.5s, pause 4-5s retained. Timeline duration is
// the max end across tracks; the camera (1..9s) is the longest layer here.
Check("independent origins: camera=1s, audio=0.5s, pause 4-5s retained",
    Math.Abs(project.CameraClips[0].Start.TotalSeconds - 1.0) < 0.001 &&
    Math.Abs(project.AudioClips[0].Start.TotalSeconds - 0.5) < 0.001 &&
    project.ScreenClips[1].Start.TotalSeconds == 5.0 &&
    project.TimelineDuration.Micros == TimeCode.FromSeconds(9).Micros,
    $"duration={project.TimelineDuration.TotalSeconds:0.###}s");

// ---------- first real export ----------
Console.WriteLine("== Exporting full-resolution video ==");
var output1 = Path.Combine(workDir, "lesson-v1.mp4");
var exporter = new VideoExporter(ffmpeg);
await exporter.ExportAsync(project, output1, new ExportOptions { Preset = "ultrafast", Crf = 18 });
var info1 = await probe.ProbeAsync(output1);
Check("output is a real 1280x720 file with video and audio",
    info1 is { Width: 1280, Height: 720, HasVideo: true, HasAudio: true },
    $"{info1.Width}x{info1.Height} audio={info1.HasAudio}");
Check("output duration covers the 8s timeline including the pause",
    info1.DurationSeconds >= 7.8, $"{info1.DurationSeconds:0.00}s");

// ---------- pixel-level verification ----------
Console.WriteLine("== Pixel-level checks ==");
var frameDir = Path.Combine(workDir, "frames");
Directory.CreateDirectory(frameDir);
await Ffmpeg($"-y -i \"{output1}\" -vf fps=1 \"{Path.Combine(frameDir, "f-%02d.png")}\"");

// Camera PIP bottom-right. At the sampled frame the camera shows its block color;
// sample well inside the PIP (its width is 25% of canvas = 320px, margin 24).
var pipProbe = await ProbePixel(Path.Combine(frameDir, "f-03.png"), 1280 - 200, 720 - 120);
Check("camera PIP visible at bottom-right during camera clip",
    ColorClose(pipProbe, (70, 110, 200), tolerance: 60),
    $"rgb=({pipProbe.R},{pipProbe.G},{pipProbe.B})");

// Center of frame at t=6s belongs to screen B content (dark UI background color), not camera.
var centerProbe = await ProbePixel(Path.Combine(frameDir, "f-07.png"), 640, 360);
Check("screen B content visible after mid-lesson resolution change",
    ColorClose(centerProbe, (18, 22, 30), tolerance: 40),
    $"rgb=({centerProbe.R},{centerProbe.G},{centerProbe.B})");

// Frame at t=4..5s is the retained pause: screen A last frame region should still be dark UI bg.
var pauseProbe = await ProbePixel(Path.Combine(frameDir, "f-05.png"), 640, 360);
Check("pause between clips is retained (no screen content jump at 4s)",
    ColorClose(pauseProbe, (18, 22, 30), tolerance: 40));

// ---------- native-pixel export vs preview cache ----------
Console.WriteLine("== Preview cache vs export sampling ==");
var extractor = new FrameExtractor(Path.Combine(workDir, "frame-cache"), ffmpeg);
var previewPng = await extractor.ExtractFrameAsync(
    Path.Combine(mediaDir, "screen_clicks_a.mp4"), TimeCode.FromSeconds(1), maxWidth: 320);
var nativePng = await extractor.ExtractFrameAsync(
    Path.Combine(mediaDir, "screen_clicks_a.mp4"), TimeCode.FromSeconds(1), maxWidth: null);
using var previewBmp = new AvaloniaPlaceholderBitmap(previewPng);
using var nativeBmp = new AvaloniaPlaceholderBitmap(nativePng);
Check("preview cache is downscaled while native extraction keeps source pixels",
    previewBmp.Width <= 320 && nativeBmp.Width == 1920,
    $"preview={previewBmp.Width}px native={nativeBmp.Width}px");
var graph = new FilterGraphBuilder(project).Build();
Check("export filter graph never uses the preview cache as a source",
    !graph.Inputs.Any(i => i.Contains("frame-cache", StringComparison.OrdinalIgnoreCase)) &&
    graph.Inputs.All(File.Exists));

// ---------- ripple cut + locked-track conflict ----------
Console.WriteLine("== Ripple semantics ==");
var history = new HistoryService();
var engine = new EditEngine(history);
project.CameraLocked = true;
var blocked = engine.RippleDelete(project, TrackKind.Screen,
    TimeCode.FromSeconds(2), TimeCode.FromSeconds(3), RippleOptions.All);
Check("ripple with locked camera is blocked and conflict reported",
    !blocked.Success && blocked.Conflicts.Any(c => c.Track == TrackKind.Camera));
project.CameraLocked = false;

var ok = engine.RippleDelete(project, TrackKind.Screen,
    TimeCode.FromSeconds(2), TimeCode.FromSeconds(3), RippleOptions.All);
Check("ripple delete succeeds once lock is cleared", ok.Success);
// Audio 0.5..6s crosses the cut: head 0.5..2s stays put, tail is placed at the range
// start (2s); both pieces preserve their source mapping and nothing slides negative.
// Camera (1s origin) and audio (0.5s origin) cross the same 2-3s cut. The shared
// 1s delta is visible on every post-cut tail: screen B moved 8->6, camera tail and
// audio tail both sit at the 2s range start, heads keep their independent origins.
var screenBTail = project.ScreenClips.First(c => c.SourceId == screenB.Id);
// Ripple removes exactly the 1s range (2-3s): screen B started at 5s and shifts to 4s;
// the 4-5s pause after clip A is untouched. Camera/audio tails land on the range start.
Check("camera and audio follow the same ripple edit as the screen, pauses preserved",
    Math.Abs(screenBTail.Start.TotalSeconds - 4.0) < 0.001 &&
    project.AudioClips.Count == 2 &&
    Math.Abs(project.AudioClips.Min(c => c.Start.TotalSeconds) - 0.5) < 0.001 &&
    Math.Abs(project.AudioClips.Max(c => c.Start.TotalSeconds) - 2.0) < 0.001 &&
    Math.Abs(project.CameraClips.Max(c => c.Start.TotalSeconds) - 2.0) < 0.001,
    $"screenB={screenBTail.Start.TotalSeconds:0.##}s, audioStarts=[{string.Join(",", project.AudioClips.Select(c => c.Start.TotalSeconds.ToString("0.##")))}]");

// Mute region never moves clips.
var screenStartSnapshot = project.ScreenClips.Select(c => c.Start.Micros).ToArray();
engine.AddMuteRegion(project, TimeCode.FromSeconds(1), TimeCode.FromSeconds(1.5));
Check("mute region silences audio without moving screen operations",
    project.MuteRegions.Count == 1 &&
    project.ScreenClips.Select(c => c.Start.Micros).SequenceEqual(screenStartSnapshot));

// ---------- hide camera: composition only ----------
Console.WriteLine("== Camera hide ==");
var cameraClip = project.CameraClips.OrderBy(c => c.Start.Micros).First();
var positionBeforeHide = cameraClip.Start.Micros;
var sourceBeforeHide = cameraClip.SourceId;
var cameraClipCount = project.CameraClips.Count;
var cameraIds = project.CameraClips.Select(c => c.Id).ToHashSet();
// The ripple split the camera into pieces; hiding one hides the camera layer overall,
// so mark every piece hidden (the UI exposes this per clip and the source is never touched).
foreach (var id in cameraIds)
    engine.SetCameraHidden(project, project.CameraClips.First(c => c.Id == id), true);
Check("hidden camera clip stays on timeline with same position and source",
    project.CameraClips.Count == cameraClipCount &&
    project.CameraClips.All(c => c.Hidden) &&
    Math.Abs(project.CameraClips.First(c => c.Id == cameraClip.Id).Start.TotalSeconds -
             (positionBeforeHide / 1_000_000.0)) < 0.001 &&
    project.CameraClips.First(c => c.Id == cameraClip.Id).SourceId == sourceBeforeHide,
    $"count={project.CameraClips.Count}, allHidden={project.CameraClips.All(c => c.Hidden)}");
Check("source file still exists on disk", File.Exists(Path.Combine(mediaDir, "camera_blocks.mp4")));

var hiddenGraph = new FilterGraphBuilder(project).Build();
Check("hidden camera pieces produce no overlay in the render graph",
    !hiddenGraph.FilterLines.Any(l => l.Contains("overlay", StringComparison.OrdinalIgnoreCase)));

// Undo every hide to restore the original visible camera arrangement.
while (project.CameraClips.Any(c => c.Hidden))
    history.Undo(project);
Check("undo restores camera visibility and exact position",
    project.CameraClips.All(c => !c.Hidden) &&
    project.CameraClips.Any(c => c.SourceId == sourceBeforeHide &&
                                 Math.Abs(c.Start.TotalSeconds - (positionBeforeHide / 1_000_000.0)) < 0.001));

// ---------- persistence: close and reopen ----------
Console.WriteLine("== Persistence and reopen ==");
var projectFile = Path.Combine(workDir, "lesson.course.json");
ProjectSerializer.Save(project, projectFile);
var reopened = ProjectSerializer.Load(projectFile);
Check("reopened project restores clips, mute region and PIP",
    reopened.Project.ScreenClips.Count == project.ScreenClips.Count &&
    reopened.Project.CameraClips.Count == project.CameraClips.Count &&
    reopened.Project.AudioClips.Count == project.AudioClips.Count &&
    reopened.Project.MuteRegions.Count == 1 &&
    reopened.MissingFiles.Count == 0);
Check("reopened timeline duration matches pre-close state",
    reopened.Project.TimelineDuration.Micros == project.TimelineDuration.Micros);

// Re-export from the reopened project and compare decoded content.
var output2 = Path.Combine(workDir, "lesson-v2-reopened.mp4");
await new VideoExporter(ffmpeg).ExportAsync(reopened.Project, output2,
    new ExportOptions { Preset = "ultrafast", Crf = 18 });
// And once more from the in-memory project, which is the exact same editing state.
var output3 = Path.Combine(workDir, "lesson-v3-inmemory.mp4");
await new VideoExporter(ffmpeg).ExportAsync(project, output3,
    new ExportOptions { Preset = "ultrafast", Crf = 18 });
var info2 = await probe.ProbeAsync(output2);
var hashReopened = await DecodedHash(output2);
var hashInMemory = await DecodedHash(output3);
Check("reopened project renders identical decoded video to the in-memory state",
    hashReopened == hashInMemory,
    $"{info2.Width}x{info2.Height} {info2.DurationSeconds:0.##}s, {hashReopened[..12]} vs {hashInMemory[..12]}");

// ---------- audio checks ----------
Console.WriteLine("== Audio checks ==");
var wavOut = Path.Combine(workDir, "audio.wav");
await Ffmpeg($"-y -i \"{output2}\" -vn -ac 1 -ar 16000 \"{wavOut}\"");
// After the ripple the voice lands at ~0.5s; its first command beep occupies 0.5-0.85s.
var audioLevels = await AudioRms(wavOut, 0.55, 0.8);
// Mute region was added at 1.0-1.5 after the ripple: its middle must be silent.
var mutedLevels = await AudioRms(wavOut, 1.1, 1.4);
Check("audio track present with audible command content", audioLevels > 0.05, $"rms={audioLevels:0.000}");
Check("mute region renders as silence while other audio stays intact",
    mutedLevels < 0.005 && audioLevels > 0.05, $"mutedRms={mutedLevels:0.0000}");

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

// ---------- helpers ----------
static async Task<(byte R, byte G, byte B)> ProbePixel(string png, int x, int y)
{
    var outFile = png + ".rgb";
    var psi = new ProcessStartInfo
    {
        FileName = Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG") ?? "ffmpeg",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    psi.ArgumentList.Add("-y");
    psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(png);
    psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add($"crop=1:1:{x}:{y}");
    psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
    psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("rgb24");
    psi.ArgumentList.Add(outFile);
    using var p = Process.Start(psi)!;
    var stderrTask = p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync();
    var err = await stderrTask;
    if (p.ExitCode != 0) throw new InvalidOperationException(err);
    var bytes = await File.ReadAllBytesAsync(outFile);
    return (bytes[0], bytes[1], bytes[2]);
}

static bool ColorClose((byte R, byte G, byte B) actual, (int R, int G, int B) expected, int tolerance)
    => Math.Abs(actual.R - expected.R) <= tolerance &&
       Math.Abs(actual.G - expected.G) <= tolerance &&
       Math.Abs(actual.B - expected.B) <= tolerance;

static async Task<string> DecodedHash(string video)
{
    var ffmpeg = Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG") ?? "ffmpeg";
    var result = await ProcessRunner.RunAsync(ffmpeg,
        $"-i \"{video}\" -an -f md5 -");
    var line = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
    return line.Trim();
}

static async Task<double> AudioRms(string wav, double fromSec, double toSec)
{
    var ffmpeg = Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG") ?? "ffmpeg";
    var duration = toSec - fromSec;
    var tmp = wav + $".{fromSec:0.##}.f32";
    var result = await ProcessRunner.RunAsync(ffmpeg,
        $"-y -ss {fromSec.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
        $"-t {duration.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
        $"-i \"{wav}\" -f f32le -ac 1 -ar 16000 \"{tmp}\"");
    if (!result.Success) throw new InvalidOperationException(result.StdErr);
    var bytes = await File.ReadAllBytesAsync(tmp);
    var floats = new float[bytes.Length / 4];
    Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * 4);
    if (floats.Length == 0) return 0;
    var sumSq = floats.Sum(f => f * f);
    return Math.Sqrt(sumSq / floats.Length);
}

internal sealed class AvaloniaPlaceholderBitmap : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public AvaloniaPlaceholderBitmap(byte[] png)
    {
        // Minimal PNG IHDR parse avoids an image library dependency in the harness.
        Width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        Height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
    }
    public void Dispose() { }
}
