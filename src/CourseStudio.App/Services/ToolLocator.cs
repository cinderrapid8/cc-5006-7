namespace CourseStudio.App.Services;

/// <summary>Resolves ffmpeg/ffprobe binaries: env var, PATH, or a user-configured folder.</summary>
public static class ToolLocator
{
    public static string Ffmpeg =>
        Environment.GetEnvironmentVariable("COURSESTUDIO_FFMPEG") ?? Find("ffmpeg");

    public static string Ffprobe =>
        Environment.GetEnvironmentVariable("COURSESTUDIO_FFPROBE") ?? Find("ffprobe");

    private static string Find(string name)
    {
        var ext = OperatingSystem.IsWindows() ? ".exe" : "";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, name + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return name + ext;
    }
}
