using System.Text.Json;
using System.Text.Json.Serialization;
using CourseStudio.Core.Models;

namespace CourseStudio.Core.Persistence;

public sealed class ProjectLoadResult
{
    public required CourseProject Project { get; init; }
    public List<string> MissingFiles { get; } = new();
}

/// <summary>
/// Saves and restores the complete editing state. Media paths are stored relative
/// to the project file when possible so a moved project folder keeps working.
/// </summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(CourseProject project, string projectFilePath)
    {
        var dto = ProjectDto.From(project, projectFilePath);
        var json = JsonSerializer.Serialize(dto, Options);
        File.WriteAllText(projectFilePath, json);
    }

    public static ProjectLoadResult Load(string projectFilePath)
    {
        var json = File.ReadAllText(projectFilePath);
        var dto = JsonSerializer.Deserialize<ProjectDto>(json, Options)
            ?? throw new InvalidDataException("Project file is empty or invalid.");
        var result = dto.ToProject(projectFilePath);
        return result;
    }
}

internal sealed class SourceDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Path { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long DurationMicros { get; set; }
}

internal sealed class ClipDto
{
    public string Id { get; set; } = "";
    public string Track { get; set; } = "";
    public string SourceId { get; set; } = "";
    public long StartMicros { get; set; }
    public long SourceInMicros { get; set; }
    public long SourceOutMicros { get; set; }
    public string? ResizeMode { get; set; }
    public string? BackgroundHex { get; set; }
    public bool Hidden { get; set; }
    public double Gain { get; set; } = 1.0;
}

internal sealed class MuteDto
{
    public string Id { get; set; } = "";
    public long StartMicros { get; set; }
    public long EndMicros { get; set; }
}

internal sealed class PipDto
{
    public string Corner { get; set; } = "BottomRight";
    public double WidthRatio { get; set; } = 0.25;
    public int Margin { get; set; } = 24;
    public double Opacity { get; set; } = 1.0;
}

internal sealed class ProjectDto
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public int FrameRate { get; set; }
    public string BackgroundHex { get; set; } = "#101014";
    public bool ScreenLocked { get; set; }
    public bool CameraLocked { get; set; }
    public bool AudioLocked { get; set; }
    public PipDto Pip { get; set; } = new();
    public List<SourceDto> Sources { get; set; } = new();
    public List<ClipDto> Clips { get; set; } = new();
    public List<MuteDto> MuteRegions { get; set; } = new();

    public static ProjectDto From(CourseProject p, string projectFilePath)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) ?? ".";
        string StorePath(string filePath)
        {
            var full = Path.GetFullPath(filePath);
            var fullDir = Path.GetPathRoot(full) is not null && full.StartsWith(projectDir, StringComparison.Ordinal)
                ? Path.GetRelativePath(projectDir, full)
                : full;
            return fullDir.Replace('\\', '/');
        }

        return new ProjectDto
        {
            Name = p.Name,
            CanvasWidth = p.CanvasWidth,
            CanvasHeight = p.CanvasHeight,
            FrameRate = p.FrameRate,
            BackgroundHex = p.BackgroundHex,
            ScreenLocked = p.ScreenLocked,
            CameraLocked = p.CameraLocked,
            AudioLocked = p.AudioLocked,
            Pip = new PipDto
            {
                Corner = p.Pip.Corner.ToString(),
                WidthRatio = p.Pip.WidthRatio,
                Margin = p.Pip.Margin,
                Opacity = p.Pip.Opacity
            },
            Sources = p.Sources.Select(s => new SourceDto
            {
                Id = s.Id,
                Kind = s.Kind.ToString(),
                Path = StorePath(s.FilePath),
                Width = s.NaturalWidth,
                Height = s.NaturalHeight,
                DurationMicros = s.Duration.Micros
            }).ToList(),
            Clips = p.AllClips.Select(c =>
            {
                var d = new ClipDto
                {
                    Id = c.Id,
                    Track = c.Track.ToString(),
                    SourceId = c.SourceId,
                    StartMicros = c.Start.Micros,
                    SourceInMicros = c.SourceIn.Micros,
                    SourceOutMicros = c.SourceOut.Micros
                };
                switch (c)
                {
                    case ScreenClip sc:
                        d.ResizeMode = sc.ResizeMode.ToString();
                        d.BackgroundHex = sc.BackgroundHex;
                        break;
                    case CameraClip cc:
                        d.Hidden = cc.Hidden;
                        break;
                    case AudioClip ac:
                        d.Gain = ac.Gain;
                        break;
                }
                return d;
            }).ToList(),
            MuteRegions = p.MuteRegions.Select(m => new MuteDto
            {
                Id = m.Id,
                StartMicros = m.Start.Micros,
                EndMicros = m.End.Micros
            }).ToList()
        };
    }

    public ProjectLoadResult ToProject(string projectFilePath)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) ?? ".";
        var p = new CourseProject
        {
            Name = Name,
            CanvasWidth = CanvasWidth,
            CanvasHeight = CanvasHeight,
            FrameRate = FrameRate,
            BackgroundHex = BackgroundHex,
            ScreenLocked = ScreenLocked,
            CameraLocked = CameraLocked,
            AudioLocked = AudioLocked,
            Pip = new PipSettings
            {
                Corner = Enum.Parse<PipCorner>(Pip.Corner),
                WidthRatio = Pip.WidthRatio,
                Margin = Pip.Margin,
                Opacity = Pip.Opacity
            }
        };

        var result = new ProjectLoadResult { Project = p };

        foreach (var s in Sources)
        {
            var resolved = Path.IsPathRooted(s.Path)
                ? s.Path
                : Path.GetFullPath(Path.Combine(projectDir, s.Path));
            if (!File.Exists(resolved)) result.MissingFiles.Add(resolved);
            p.Sources.Add(new MediaSource
            {
                Id = s.Id,
                Kind = Enum.Parse<MediaKind>(s.Kind),
                FilePath = resolved,
                NaturalWidth = s.Width,
                NaturalHeight = s.Height,
                Duration = new TimeCode(s.DurationMicros)
            });
        }

        foreach (var c in Clips)
        {
            var track = Enum.Parse<TrackKind>(c.Track);
            Clip clip = track switch
            {
                TrackKind.Screen => new ScreenClip
                {
                    ResizeMode = Enum.Parse<ResizeMode>(c.ResizeMode ?? nameof(ResizeMode.Fit)),
                    BackgroundHex = c.BackgroundHex ?? "#000000"
                },
                TrackKind.Camera => new CameraClip { Hidden = c.Hidden },
                _ => new AudioClip { Gain = c.Gain }
            };
            clip.Id = c.Id;
            clip.SourceId = c.SourceId;
            clip.Start = new TimeCode(c.StartMicros);
            clip.SourceIn = new TimeCode(c.SourceInMicros);
            clip.SourceOut = new TimeCode(c.SourceOutMicros);

            switch (clip)
            {
                case ScreenClip sc: p.ScreenClips.Add(sc); break;
                case CameraClip cc: p.CameraClips.Add(cc); break;
                case AudioClip ac: p.AudioClips.Add(ac); break;
            }
        }

        foreach (var m in MuteRegions)
        {
            p.MuteRegions.Add(new MuteRegion
            {
                Id = m.Id,
                Start = new TimeCode(m.StartMicros),
                End = new TimeCode(m.EndMicros)
            });
        }

        return result;
    }
}
