using TVManager.Models;

namespace TVManager.Services.Connections;

internal interface IDeviceController
{
    DevicePlatform Platform { get; }

    Task<TvConnection> ConnectAsync(TvDevice device, CancellationToken token);
    Task DisconnectAsync(TvConnection connection, CancellationToken token);

    Task<DeviceInfo> GetInfoAsync(TvConnection connection, CancellationToken token);
    Task<IReadOnlyList<AppInfo>> GetAppsAsync(TvConnection connection, CancellationToken token);
    Task<IReadOnlyList<SettingInfo>> GetSettingsAsync(TvConnection connection, CancellationToken token);

    Task<ChangeResult> ApplySettingAsync(TvConnection connection, string key, string value,
        bool previewOnly, CancellationToken token);
    Task<ChangeResult> UndoSettingAsync(TvConnection connection, SettingChange change, CancellationToken token);
    Task<ChangeResult> UninstallAppAsync(TvConnection connection, AppInfo app,
        bool previewOnly, CancellationToken token);
}
