namespace TVManager.Models;

public sealed record DeviceInfo(
    string Manufacturer,
    string Model,
    string Firmware,
    string PlatformVersion,
    string Serial,
    string Resolution,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, string> Raw)
{
    public static DeviceInfo Empty(string note) =>
        new("—", "—", "—", "—", "—", "—", [note], new Dictionary<string, string>());
}
