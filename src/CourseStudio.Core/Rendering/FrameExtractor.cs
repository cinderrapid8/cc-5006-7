using CourseStudio.Core.Media;
using CourseStudio.Core.Models;

namespace CourseStudio.Core.Rendering;

/// <summary>
/// Extracts preview frames from the ORIGINAL source files at native resolution,
/// then downscales only for display. The cache key includes the source path and
/// native timestamp, so shrinking the preview window can never cause an export
/// to reuse low-resolution imagery (export goes through FilterGraphBuilder, not
/// this cache at all).
/// </summary>
public sealed class FrameExtractor
{
    private readonly string _ffmpeg;
    private readonly string _cacheDir;

    public FrameExtractor(string? cacheDir = null, string? ffmpegPath = null)
    {
        _ffmpeg = ffmpegPath ?? Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG")
            ?? FindOnPath("ffmpeg");
        _cacheDir = cacheDir ?? Path.Combine(Path.GetTempPath(), "coursestudio-frames");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Grab one frame from a source at its own timeline position, returned as PNG bytes.</summary>
    public async Task<byte[]> ExtractFrameAsync(string sourcePath, TimeCode sourcePosition,
        int? maxWidth = null, CancellationToken ct = default)
    {
        var key = $"{StableHash(sourcePath)}_{sourcePosition.Micros}_{maxWidth?.ToString() ?? "native"}.png";
        var cachePath = Path.Combine(_cacheDir, key);
        if (File.Exists(cachePath)) return await File.ReadAllBytesAsync(cachePath, ct);

        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-ss", sourcePosition.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-i", sourcePath,
            "-frames:v", "1"
        };
        if (maxWidth is { } w and > 0)
        {
            args.Add("-vf");
            args.Add($"scale='min({w},iw)':-2");
        }
        args.Add(cachePath);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _ffmpeg,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0 || !File.Exists(cachePath))
            throw new InvalidOperationException($"Frame extraction failed: {stderr}");
        return await File.ReadAllBytesAsync(cachePath, ct);
    }

    /// <summary>Resolve which clip of a track covers a project time and the matching source timestamp.</summary>
    public static (Clip clip, TimeCode sourcePosition)? ResolveAt(IEnumerable<Clip> clips, TimeCode projectTime)
    {
        foreach (var clip in clips.OrderBy(c => c.Start.Micros))
        {
            if (projectTime.Micros >= clip.Start.Micros && projectTime.Micros < clip.End.Micros)
            {
                var offset = projectTime.Micros - clip.Start.Micros;
                return (clip, new TimeCode(clip.SourceIn.Micros + offset));
            }
        }
        return null;
    }

    public void ClearCache()
    {
        if (!Directory.Exists(_cacheDir)) return;
        foreach (var file in Directory.EnumerateFiles(_cacheDir, "*.png"))
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
    }

    private static string StableHash(string value)
    {
        // FNV-1a 64-bit: stable across runs, no extra package dependency.
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash.ToString("x16");
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
