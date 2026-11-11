namespace CourseStudio.Core.Models;

/// <summary>
/// A time range on the audio timeline where the mix is silenced. Muting never
/// deletes time: screen and camera clips keep their positions.
/// </summary>
public sealed class MuteRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TimeCode Start { get; set; }
    public TimeCode End { get; set; }

    public TimeCode Duration => new(End.Micros - Start.Micros);

    public MuteRegion Clone() => new() { Id = Id, Start = Start, End = End };
}
