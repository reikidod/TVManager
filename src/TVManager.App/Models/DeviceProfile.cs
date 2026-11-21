namespace TVManager.Models;

public sealed record DeviceProfile(
    string Name,
    string Platform,
    DateTimeOffset CreatedAt,
    Dictionary<string, string> Settings);
