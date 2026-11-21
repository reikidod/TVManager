using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TVManager.Models;

namespace TVManager.Services.Connections;

internal sealed partial class WebOsController : IDeviceController
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);

    public DevicePlatform Platform => DevicePlatform.LgWebOs;

    public async Task<TvConnection> ConnectAsync(TvDevice device, CancellationToken token)
    {
        var descriptionUri = TryGetDescriptionUri(device);
        if (descriptionUri is null)
            throw new InvalidOperationException("Для LG webOS не найден SSDP LOCATION. Повторите поиск устройства.");

        var description = await ReadDescriptionAsync(descriptionUri, token);
        if (description.Count == 0)
            throw new InvalidOperationException($"Не удалось прочитать SSDP-описание устройства {descriptionUri.Host}.");

        var capabilities = new DeviceCapabilities(
            CanReadInfo: true,
            CanManageApps: false,
            CanChangeDisplaySound: false,
            CanReadNetworkInputs: false,
            CanUseProfilesAndRollback: false);

        return new TvConnection(device, capabilities, descriptionUri.Host)
        {
            ConnectedAt = DateTimeOffset.Now,
            State = ConnectionState.Connected,
            StatusText = "webOS: SSDP-описание получено"
        };
    }

    public Task DisconnectAsync(TvConnection connection, CancellationToken token) => Task.CompletedTask;

    public async Task<DeviceInfo> GetInfoAsync(TvConnection connection, CancellationToken token)
    {
        var uri = TryGetDescriptionUri(connection.Device);
        var description = uri is null ? new Dictionary<string, string>() : await ReadDescriptionAsync(uri, token);

        var notes = new List<string>
        {
            "Для управления приложениями и настройками webOS требуется developer-режим и официальный webOS CLI; в этой версии доступно только чтение сведений."
        };

        return new DeviceInfo(
            Get(description, "manufacturer", "LG"),
            Get(description, "modelName", connection.Device.Name),
            Get(description, "deviceVersion", Get(description, "modelNumber", "—")),
            "webOS",
            Get(description, "serialNumber", connection.Endpoint),
            "—",
            notes,
            description);
    }

    public Task<IReadOnlyList<AppInfo>> GetAppsAsync(TvConnection connection, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<AppInfo>>(Array.Empty<AppInfo>());

    public Task<IReadOnlyList<SettingInfo>> GetSettingsAsync(TvConnection connection, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<SettingInfo>>([
            new SettingInfo("network.address", "Адрес подключения", connection.Endpoint,
                "IP-адрес из SSDP-описания. Только чтение.", false)
        ]);

    public Task<ChangeResult> ApplySettingAsync(TvConnection connection, string key, string value,
        bool previewOnly, CancellationToken token) =>
        Task.FromResult(ControllerHelpers.Unsupported("Изменение настроек", "LG webOS"));

    public Task<ChangeResult> UndoSettingAsync(TvConnection connection, SettingChange change, CancellationToken token) =>
        Task.FromResult(ControllerHelpers.Unsupported("Откат настроек", "LG webOS"));

    public Task<ChangeResult> UninstallAppAsync(TvConnection connection, AppInfo app,
        bool previewOnly, CancellationToken token) =>
        Task.FromResult(ControllerHelpers.Unsupported("Удаление приложений", "LG webOS"));

    private static Uri? TryGetDescriptionUri(TvDevice device)
    {
        var match = LocationPattern().Match(device.Evidence ?? string.Empty);
        return match.Success && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static async Task<Dictionary<string, string>> ReadDescriptionAsync(Uri uri, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = HttpTimeout };
        try
        {
            using var response = await client.GetAsync(uri, token);
            if (!response.IsSuccessStatusCode) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, token);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "manufacturer", "modelName", "modelNumber", "serialNumber", "deviceVersion", "friendlyName" })
            {
                var value = document.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
                if (!string.IsNullOrEmpty(value)) result[name] = value;
            }
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value : fallback;

    [GeneratedRegex(@"LOCATION:\s*(?<url>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex LocationPattern();
}
