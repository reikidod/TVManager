using System.IO;
using System.Text.RegularExpressions;
using TVManager.Models;

namespace TVManager.Services.Connections;

internal sealed partial class AdbController : IDeviceController
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(6);

    public DevicePlatform Platform => DevicePlatform.AndroidFamily;

    public async Task<TvConnection> ConnectAsync(TvDevice device, CancellationToken token)
    {
        var adb = ToolLocator.FindOnPath("adb")
            ?? throw new InvalidOperationException("ADB не найден. Запустите scripts\\Install-DeviceTools.ps1 или добавьте platform-tools в PATH.");

        var endpoint = device.Address.Trim();
        var isNetwork = endpoint.Contains(':') || device.Connection == ConnectionKind.Network;
        ProcessResult? result;

        if (isNetwork)
        {
            result = await ProcessRunner.RunAsync(adb, ["connect", endpoint], ShortTimeout, token);
            var text = $"{result?.StandardOutput} {result?.StandardError}";
            if (result is null || !text.Contains("connected to", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Не удалось подключиться к {endpoint} через ADB: {text.Trim()}");
        }
        else
        {
            result = await ProcessRunner.RunAsync(adb, ["-s", endpoint, "get-serialno"], ShortTimeout, token);
            if (result is null || !result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
                throw new InvalidOperationException($"ADB-устройство {endpoint} недоступно. Проверьте USB-отладку и авторизацию компьютера.");
        }

        var capabilities = new DeviceCapabilities(
            CanReadInfo: true,
            CanManageApps: true,
            CanChangeDisplaySound: true,
            CanReadNetworkInputs: true,
            CanUseProfilesAndRollback: true);

        var connection = new TvConnection(device, capabilities, endpoint)
        {
            ConnectedAt = DateTimeOffset.Now,
            State = ConnectionState.Connected,
            StatusText = isNetwork ? "ADB: беспроводное подключение установлено" : "ADB: USB-подключение подтверждено"
        };
        return connection;
    }

    public Task DisconnectAsync(TvConnection connection, CancellationToken token)
    {
        if (connection.Endpoint.Contains(':'))
        {
            var adb = ToolLocator.FindOnPath("adb");
            if (adb is not null)
                return ProcessRunner.RunAsync(adb, ["disconnect", connection.Endpoint], ShortTimeout, token);
        }
        return Task.CompletedTask;
    }

    public async Task<DeviceInfo> GetInfoAsync(TvConnection connection, CancellationToken token)
    {
        var props = await RunAsync(connection, ["shell", "getprop"], token);
        var values = ParseProperties(props?.StandardOutput ?? string.Empty);

        var resolution = "—";
        var sizeResult = await RunAsync(connection, ["shell", "wm", "size"], token);
        var sizeMatch = SizePattern().Match(sizeResult?.StandardOutput ?? string.Empty);
        if (sizeMatch.Success) resolution = sizeMatch.Groups["size"].Value;

        var release = values.GetValueOrDefault("ro.build.version.release", "—");
        var buildId = values.GetValueOrDefault("ro.build.id", string.Empty);
        var firmware = string.IsNullOrEmpty(buildId) ? release : $"{release} ({buildId})";

        var manufacturer = values.GetValueOrDefault("ro.product.manufacturer", "—");
        var model = values.GetValueOrDefault("ro.product.model", connection.Device.Name);
        var serial = values.GetValueOrDefault("ro.serialno", connection.Endpoint);
        var isFire = (props?.StandardOutput ?? string.Empty).Contains("fire_os", StringComparison.OrdinalIgnoreCase) ||
                     manufacturer.Contains("amazon", StringComparison.OrdinalIgnoreCase);

        var rawKeys = new[]
        {
            "ro.product.manufacturer", "ro.product.model", "ro.product.device", "ro.product.board",
            "ro.build.version.release", "ro.build.id", "ro.build.version.sdk", "ro.build.characteristics",
            "ro.serialno", "ro.hardware", "ro.bootloader"
        };
        var raw = rawKeys.Where(values.ContainsKey).ToDictionary(key => key, key => values[key]);

        var notes = new List<string>();
        if (isFire) notes.Add("По свойствам определена Fire OS — часть настроек Android TV может отличаться.");
        if (props is null || !props.Succeeded) notes.Add("Не удалось полностью прочитать getprop; показанные данные могут быть неполными.");

        return new DeviceInfo(manufacturer, model, firmware, $"Android {release}", serial, resolution, notes, raw);
    }

    public async Task<IReadOnlyList<AppInfo>> GetAppsAsync(TvConnection connection, CancellationToken token)
    {
        var result = await RunAsync(connection, ["shell", "pm", "list", "packages", "-3"], token);
        var apps = new List<AppInfo>();
        if (result is null || !result.Succeeded) return apps;

        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = line.Trim();
            if (!value.StartsWith("package:", StringComparison.OrdinalIgnoreCase)) continue;
            var package = value["package:".Length..].Trim();
            if (PackagePattern().IsMatch(package))
                apps.Add(new AppInfo(package, package, string.Empty, false));
        }
        return apps.OrderBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<SettingInfo>> GetSettingsAsync(TvConnection connection, CancellationToken token)
    {
        var settings = new List<SettingInfo>();
        var size = await RunAsync(connection, ["shell", "wm", "size"], token);
        var sizeMatch = SizePattern().Match(size?.StandardOutput ?? string.Empty);
        settings.Add(new SettingInfo("display.resolution", "Разрешение экрана",
            sizeMatch.Success ? sizeMatch.Groups["size"].Value : "—",
            "Формат 1920x1080 или reset. Изменение может временно прервать изображение.", true));

        settings.Add(await ReadSettingAsync(connection, "display.screen_off_timeout_ms", "Таймаут сна, мс",
            "system", "screen_off_timeout", "0 — никогда.", token));
        settings.Add(await ReadSettingAsync(connection, "sound.volume_music", "Громкость музыки",
            "system", "volume_music", "Диапазон обычно 0–15; на части устройств настройка не поддерживается.", token));
        settings.Add(await ReadSettingAsync(connection, "network.wifi_on", "Wi-Fi включён",
            "global", "wifi_on", "1 — включить, 0 — выключить. Изменять только при понимании последствий.", token));

        return settings;
    }

    public async Task<ChangeResult> ApplySettingAsync(TvConnection connection, string key, string value,
        bool previewOnly, CancellationToken token)
    {
        if (connection.State != ConnectionState.Connected)
            return new ChangeResult(false, "Сначала подключитесь к устройству.");

        if (!ValidateValue(key, value, out var validationError))
            return new ChangeResult(false, validationError);

        var previous = string.Empty;
        if (!previewOnly)
        {
            var current = await GetSettingsAsync(connection, token);
            previous = current.FirstOrDefault(x => x.Key == key)?.Value ?? string.Empty;
        }

        var args = BuildSetArguments(key, value);
        if (args is null)
            return new ChangeResult(false, $"Неизвестный параметр {key}.");

        var command = "adb -s " + connection.Endpoint + " " + string.Join(' ', args);
        var preview = new[] { command, previewOnly ? "Изменения не применялись." : "Изменение будет применено." };

        if (previewOnly)
            return new ChangeResult(true, "Предпросмотр команды.", Preview: preview);

        var result = await RunAsync(connection, args, token);
        if (result is null || !result.Succeeded)
            return new ChangeResult(false, $"Команда не выполнена: {result?.StandardError.Trim()}", Preview: preview);

        var updated = await GetSettingsAsync(connection, token);
        var appliedValue = updated.FirstOrDefault(x => x.Key == key)?.Value ?? value;
        return new ChangeResult(true, $"Параметр {key} обновлён.",
            new SettingChange(key, previous, appliedValue), preview);
    }

    public async Task<ChangeResult> UndoSettingAsync(TvConnection connection, SettingChange change, CancellationToken token) =>
        await ApplySettingAsync(connection, change.Key, change.PreviousValue ?? "null", false, token);

    public async Task<ChangeResult> UninstallAppAsync(TvConnection connection, AppInfo app,
        bool previewOnly, CancellationToken token)
    {
        if (connection.State != ConnectionState.Connected)
            return new ChangeResult(false, "Сначала подключитесь к устройству.");
        if (!PackagePattern().IsMatch(app.PackageId))
            return new ChangeResult(false, "Некорректный идентификатор пакета.");
        if (app.IsSystem)
            return new ChangeResult(false, "Системные приложения удалять запрещено.");

        var args = new[] { "uninstall", app.PackageId };
        var preview = new[] { "adb -s " + connection.Endpoint + " uninstall " + app.PackageId,
            "Перед удалением будет сделана попытка сохранить APK в локальную резервную папку." };
        if (previewOnly)
            return new ChangeResult(true, "Предпросмотр удаления.", Preview: preview);

        var backupMessage = await TryBackupAsync(connection, app.PackageId, token);
        var result = await RunAsync(connection, args, token);
        var succeeded = result is not null && result.Succeeded &&
                        result.StandardOutput.Contains("Success", StringComparison.OrdinalIgnoreCase);
        return new ChangeResult(succeeded,
            succeeded ? $"Приложение {app.PackageId} удалено. {backupMessage}" : $"Удаление не удалось: {result?.StandardOutput.Trim()} {result?.StandardError.Trim()}",
            Preview: preview);
    }

    private async Task<string> TryBackupAsync(TvConnection connection, string package, CancellationToken token)
    {
        try
        {
            var pathResult = await RunAsync(connection, ["shell", "pm", "path", package], token);
            var line = pathResult?.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (line is null || !line.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
                return "Резервную копию создать не удалось: путь APK не найден.";

            var remotePath = line["package:".Length..].Trim();
            var backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TVManager", "backups");
            Directory.CreateDirectory(backupDirectory);
            var localPath = Path.Combine(backupDirectory, package + ".apk");
            var adb = ToolLocator.FindOnPath("adb");
            if (adb is null) return "Резервную копию создать не удалось: ADB не найден.";

            var pull = await ProcessRunner.RunAsync(adb,
                ["-s", connection.Endpoint, "pull", remotePath, localPath], TimeSpan.FromSeconds(30), token);
            return pull is { Succeeded: true } ? $"Резервная копия: {localPath}" : "Резервную копию создать не удалось.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Резервную копию создать не удалось: {ex.Message}";
        }
    }

    private static async Task<SettingInfo> ReadSettingAsync(TvConnection connection, string key, string label,
        string group, string setting, string description, CancellationToken token)
    {
        var result = await RunAsync(connection, ["shell", "settings", "get", group, setting], token);
        var value = result?.StandardOutput.Trim() ?? string.Empty;
        return new SettingInfo(key, label, string.IsNullOrEmpty(value) ? "null" : value, description, true);
    }

    private static async Task<ProcessResult?> RunAsync(TvConnection connection, string[] args, CancellationToken token)
    {
        var adb = ToolLocator.FindOnPath("adb");
        if (adb is null) return null;
        var all = new List<string> { "-s", connection.Endpoint };
        all.AddRange(args);
        return await ProcessRunner.RunAsync(adb, all, ShortTimeout, token);
    }

    private static Dictionary<string, string> ParseProperties(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = PropertyPattern().Match(line.Trim());
            if (match.Success) result[match.Groups["key"].Value] = match.Groups["value"].Value;
        }
        return result;
    }

    private static string[]? BuildSetArguments(string key, string value) => key switch
    {
        "display.resolution" => new[] { "shell", "wm", "size", value },
        "display.screen_off_timeout_ms" => new[] { "shell", "settings", "put", "system", "screen_off_timeout", value },
        "sound.volume_music" => value is "null" or ""
            ? new[] { "shell", "settings", "delete", "system", "volume_music" }
            : new[] { "shell", "settings", "put", "system", "volume_music", value },
        "network.wifi_on" => value is "null" or ""
            ? new[] { "shell", "settings", "delete", "global", "wifi_on" }
            : new[] { "shell", "settings", "put", "global", "wifi_on", value },
        _ => null
    };

    private static bool ValidateValue(string key, string value, out string error)
    {
        error = string.Empty;
        switch (key)
        {
            case "display.resolution":
                if (!ResolutionPattern().IsMatch(value))
                {
                    error = "Разрешение должно быть в формате 1920x1080 или reset.";
                    return false;
                }
                break;
            case "display.screen_off_timeout_ms":
                if (!int.TryParse(value, out var timeout) || timeout < 0)
                {
                    error = "Таймаут должен быть неотрицательным целым числом.";
                    return false;
                }
                break;
            case "sound.volume_music":
                if (value is not ("null" or "") && (!int.TryParse(value, out var volume) || volume < 0 || volume > 15))
                {
                    error = "Громкость должна быть числом от 0 до 15 или null.";
                    return false;
                }
                break;
            case "network.wifi_on":
                if (value is not ("0" or "1" or "null" or ""))
                {
                    error = "Значение должно быть 0, 1 или null.";
                    return false;
                }
                break;
        }
        return true;
    }

    [GeneratedRegex(@"\[(?<key>[^\]]+)\]:\s*\[(?<value>[^\]]*)\]")]
    private static partial Regex PropertyPattern();

    [GeneratedRegex(@"(?<size>\d+x\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SizePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_.-]+$")]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"^(?:\d+x\d+|reset)$", RegexOptions.IgnoreCase)]
    private static partial Regex ResolutionPattern();
}
