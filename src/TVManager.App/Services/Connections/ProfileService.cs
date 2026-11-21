using System.IO;
using System.Text.Json;
using TVManager.Models;

namespace TVManager.Services.Connections;

internal sealed class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<DeviceProfile> LoadAll()
    {
        var path = ProfilesPath;
        if (!File.Exists(path)) return Array.Empty<DeviceProfile>();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<DeviceProfile>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(DeviceProfile profile)
    {
        var profiles = LoadAll().Where(x => !x.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        profiles.Add(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
        File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(profiles, JsonOptions));
    }

    public void Delete(string name)
    {
        var profiles = LoadAll().Where(x => !x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
        File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(profiles, JsonOptions));
    }

    public DeviceProfile Snapshot(string name, TvConnection connection, IEnumerable<SettingInfo> settings) =>
        new(name, connection.Platform.ToString(), DateTimeOffset.Now,
            settings.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));

    private static string ProfilesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TVManager", "profiles", "profiles.json");
}
