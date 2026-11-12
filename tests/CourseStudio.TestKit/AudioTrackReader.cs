using System.Diagnostics;

namespace CourseStudio.TestKit;

/// <summary>
/// Decodes the audio track of an export to mono floats and measures where sound
/// actually lands. Used to assert A/V deviation directly on the product file.
/// </summary>
public sealed class AudioTrackReader
{
    public int SampleRate { get; }
    public float[] Samples { get; }
    public double DurationSeconds => Samples.Length / (double)SampleRate;

    private AudioTrackReader(float[] samples, int sampleRate)
    {
        Samples = samples;
        SampleRate = sampleRate;
    }

    public static AudioTrackReader Decode(string videoPath, int sampleRate = 16000, string? ffmpeg = null)
    {
        ffmpeg ??= FfmpegTools.Ffmpeg;
        var tmp = Path.Combine(Path.GetTempPath(), $"audio-{Guid.NewGuid():N}.f32");
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
                         "-i", videoPath, "-vn", "-ac", "1",
                         "-ar", sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                         "-f", "f32le", tmp
                     })
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg audio decode failed: {stderr.GetAwaiter().GetResult()}");
            var bytes = File.ReadAllBytes(tmp);
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * 4);
            return new AudioTrackReader(floats, sampleRate);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// Rising-edge onset detector over short-time energy. Returns onset times in
    /// seconds; two onsets are merged when closer than minGapSeconds.
    /// </summary>
    public double[] DetectOnsets(double threshold = 0.02, double minGapSeconds = 0.4)
    {
        const int window = 512;
        const int hop = 256;
        var onsets = new List<double>();
        var lastOnset = double.NegativeInfinity;
        var belowSince = 0.0;
        for (var start = 0; start + window <= Samples.Length; start += hop)
        {
            double energy = 0;
            for (var i = start; i < start + window; i++) energy += Samples[i] * Samples[i];
            energy /= window;
            var t = (start + window / 2.0) / SampleRate;
            if (energy > threshold)
            {
                if (belowSince >= minGapSeconds && t - lastOnset >= minGapSeconds)
                {
                    onsets.Add(t);
                    lastOnset = t;
                }
                belowSince = 0;
            }
            else
            {
                belowSince += hop / (double)SampleRate;
            }
        }
        return onsets.ToArray();
    }

    public double Rms(double fromSeconds, double toSeconds)
    {
        var from = Math.Max(0, (int)(fromSeconds * SampleRate));
        var to = Math.Min(Samples.Length, (int)(toSeconds * SampleRate));
        if (to <= from) return 0;
        double sumSq = 0;
        for (var i = from; i < to; i++) sumSq += Samples[i] * Samples[i];
        return Math.Sqrt(sumSq / (to - from));
    }
}
