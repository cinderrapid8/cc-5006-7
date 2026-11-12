namespace CourseStudio.TestKit;

/// <summary>Locates the ffmpeg/ffprobe binaries the acceptance harness shells out to.</summary>
public static class FfmpegTools
{
    public static string Ffmpeg =>
        Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG") ?? FindOnPath("ffmpeg");

    public static string Ffprobe =>
        Environment.GetEnvironmentVariable("COURSESTUDIO_FFPROBE") ?? FindOnPath("ffprobe");

    public static bool Available()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-version");
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string FindOnPath(string name)
    {
        var ext = OperatingSystem.IsWindows() ? ".exe" : "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var bundled = Path.Combine(home, ".local", "opt", "ffmpeg", name + ext);
        if (File.Exists(bundled)) return bundled;
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, name + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return name + ext;
    }
}
