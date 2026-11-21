namespace TVManager.Models;

public sealed class DiscoveryReport
{
    public List<TvDevice> Devices { get; } = [];
    public List<string> Notes { get; } = [];
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; set; }
    public TimeSpan Duration => FinishedAt - StartedAt;
}
