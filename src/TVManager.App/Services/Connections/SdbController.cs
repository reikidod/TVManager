using System.Text.RegularExpressions;
using TVManager.Models;

namespace TVManager.Services.Connections;

internal sealed partial class SdbController : IDeviceController
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(7);

    public DevicePlatform Platform => DevicePlatform.SamsungTizen;

    public async Task<TvConnection> ConnectAsync(TvDevice device, CancellationToken token)
    {
        var sdb = ToolLocator.FindOnPath("sdb")
            ?? throw new InvalidOperationException("SDB не найден. Установите Tizen Studio / TV SDK Tools и примите лицензию Samsung.");

        var endpoint = device.Address.Trim();
        var result = await ProcessRunner.RunAsync(sdb, ["connect", endpoint], ShortTimeout, token);
        var text = $"{result?.StandardOutput} {result?.StandardError}".Trim();
        if (result is null || text.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("error", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Не удалось подключиться к {endpoint} через SDB: {text}");

        var capabilities = new DeviceCapabilities(
            CanReadInfo: true,
            CanManageApps: true,
            CanChangeDisplaySound: false,
            CanReadNetworkInputs: false,
            CanUseProfilesAndRollback: false);

        return new TvConnection(device, capabilities, endpoint)
        {
            ConnectedAt = DateTimeOffset.Now,
            State = ConnectionState.Connected,
            StatusText = "SDB: подключение установлено"
        };
    }

    public Task DisconnectAsync(TvConnection connection, CancellationToken token)
    {
        var sdb = ToolLocator.FindOnPath("sdb");
        if (sdb is null) return Task.CompletedTask;
        return ProcessRunner.RunAsync(sdb, ["disconnect", connection.Endpoint], ShortTimeout, token);
    }

    public async Task<DeviceInfo> GetInfoAsync(TvConnection connection, CancellationToken token)
    {
        var kernel = await RunAsync(connection, ["shell", "uname", "-a"], token);
        var version = await RunAsync(connection, ["shell", "cat", "/etc/version"], token);
        var kernelText = kernel?.StandardOutput.Trim() ?? string.Empty;
        var versionText = version?.StandardOutput.Trim() ?? string.Empty;

        var raw = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(kernelText)) raw["uname"] = kernelText;
        if (!string.IsNullOrEmpty(versionText)) raw["/etc/version"] = versionText;

        var sizeMatch = SizePattern().Match($"{kernelText} {versionText}");
        var notes = new List<string>();
        if (string.IsNullOrEmpty(kernelText) && string.IsNullOrEmpty(versionText))
            notes.Add("SDB подключён, но системные сведения не вернулись. Проверьте Developer Mode и регистрацию IP компьютера на телевизоре.");
        notes.Add("Samsung Tizen не предоставляет через SDB такой же набор настроек, как ADB. Неподдерживаемые операции отключены в интерфейсе.");

        return new DeviceInfo(
            "Samsung",
            connection.Device.Name,
            versionText.Length > 0 ? versionText : "не определена",
            "Tizen",
            connection.Endpoint,
            sizeMatch.Success ? sizeMatch.Groups["size"].Value : "—",
            notes,
            raw);
    }

    public async Task<IReadOnlyList<AppInfo>> GetAppsAsync(TvConnection connection, CancellationToken token)
    {
        var result = await RunAsync(connection, ["shell", "pkgcmd", "-l"], token);
        var apps = new List<AppInfo>();
        if (result is null || string.IsNullOrWhiteSpace(result.StandardOutput)) return apps;

        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains("pkgid", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = line.IndexOf(':');
            var package = (separator >= 0 ? line[(separator + 1)..] : line).Trim();
            if (!string.IsNullOrEmpty(package) && PackagePattern().IsMatch(package))
                apps.Add(new AppInfo(package, package, string.Empty, false));
        }
        return apps.OrderBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<IReadOnlyList<SettingInfo>> GetSettingsAsync(TvConnection connection, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<SettingInfo>>([
            new SettingInfo("network.address", "Адрес подключения", connection.Endpoint,
                "Адрес, указанный при подключении SDB. Только чтение.", false)
        ]);

    public Task<ChangeResult> ApplySettingAsync(TvConnection connection, string key, string value,
        bool previewOnly, CancellationToken token) =>
        Task.FromResult(ControllerHelpers.Unsupported("Изменение настроек", "Samsung Tizen"));

    public Task<ChangeResult> UndoSettingAsync(TvConnection connection, SettingChange change, CancellationToken token) =>
        Task.FromResult(ControllerHelpers.Unsupported("Откат настроек", "Samsung Tizen"));

    public Task<ChangeResult> UninstallAppAsync(TvConnection connection, AppInfo app,
        bool previewOnly, CancellationToken token) =>
        Task.FromResult(ControllerHelpers.Unsupported("Удаление приложений", "Samsung Tizen"));

    private static async Task<ProcessResult?> RunAsync(TvConnection connection, string[] args, CancellationToken token)
    {
        var sdb = ToolLocator.FindOnPath("sdb");
        if (sdb is null) return null;
        var all = new List<string> { "-s", connection.Endpoint };
        all.AddRange(args);
        return await ProcessRunner.RunAsync(sdb, all, ShortTimeout, token);
    }

    [GeneratedRegex(@"(?<size>\d+x\d+)")]
    private static partial Regex SizePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_.-]+$")]
    private static partial Regex PackagePattern();
}
