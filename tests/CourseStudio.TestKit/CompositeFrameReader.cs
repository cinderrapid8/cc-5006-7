using System.Diagnostics;

namespace CourseStudio.TestKit;

/// <summary>
/// Decodes selected frames of an exported video straight to RGB pixels. Assertions
/// read the actual composite output; nothing is re-derived from the render graph.
/// </summary>
public sealed class CompositeFrameReader
{
    private readonly Dictionary<int, RgbFrame> _frames;

    private CompositeFrameReader(Dictionary<int, RgbFrame> frames) => _frames = frames;

    public static CompositeFrameReader Open(string videoPath, int width, int height,
        IEnumerable<int> frameIndices, string? ffmpeg = null)
    {
        ffmpeg ??= FfmpegTools.Ffmpeg;
        var wanted = frameIndices.Distinct().OrderBy(i => i).ToList();
        if (wanted.Count == 0) throw new ArgumentException("At least one frame index is required.");

        var select = string.Join('+', wanted.Select(i => $"eq(n\\,{i})"));
        var tmp = Path.Combine(Path.GetTempPath(), $"composite-{Guid.NewGuid():N}.raw");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in new[]
                     {
                         "-y", "-hide_banner", "-loglevel", "error",
                         "-i", videoPath,
                         "-vf", $"select='{select}'",
                         "-vsync", "0",
                         "-f", "rawvideo", "-pix_fmt", "rgb24", tmp
                     })
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg frame decode failed: {stderr.GetAwaiter().GetResult()}");

            var raw = File.ReadAllBytes(tmp);
            var frameSize = width * height * 3;
            if (raw.Length != frameSize * wanted.Count)
                throw new InvalidOperationException(
                    $"Decoded {raw.Length / frameSize} frames but expected {wanted.Count} from {videoPath}.");
            var map = new Dictionary<int, RgbFrame>();
            for (var i = 0; i < wanted.Count; i++)
            {
                var slice = new byte[frameSize];
                Buffer.BlockCopy(raw, i * frameSize, slice, 0, frameSize);
                map[wanted[i]] = RgbFrame.Wrap(slice, width, height);
            }
            return new CompositeFrameReader(map);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    public (byte R, byte G, byte B) Pixel(int frame, int x, int y) => _frames[frame].Get(x, y);

    /// <summary>Reads a 16-bit time-marker strip by thresholding luminance at the sample points.</summary>
    public int DecodeStrip(int frame, IReadOnlyList<(int X, int Y)> samplePoints)
    {
        var value = 0;
        for (var bit = 0; bit < samplePoints.Count; bit++)
        {
            var (r, g, b) = Pixel(frame, samplePoints[bit].X, samplePoints[bit].Y);
            var luminance = (r * 299 + g * 587 + b * 114) / 1000;
            if (luminance > 145) value |= 1 << bit;
        }
        return value;
    }
}
