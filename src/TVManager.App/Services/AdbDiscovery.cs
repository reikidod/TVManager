using System.Text.RegularExpressions;
using TVManager.Models;

namespace TVManager.Services;

internal sealed partial class AdbDiscovery
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    public async Task DiscoverAsync(DiscoveryReport report, CancellationToken token)
    {
        var adb = ToolLocator.FindOnPath("adb");
        if (adb is null)
        {
            report.Notes.Add(@"ADB не найден. TV Manager проверяет локальный комплект tools\android\platform-tools, каталог %LOCALAPPDATA%\TVManager\tools и PATH. Запустите scripts\Install-DeviceTools.ps1 для установки ADB.");
            return;
        }

        var devices = await ProcessRunner.RunAsync(adb, ["devices", "-l"], CommandTimeout, token);
        if (devices is null || devices.TimedOut)
        {
            report.Notes.Add("Не удалось получить список ADB-устройств.");
            return;
        }

        foreach (var line in devices.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = AdbDeviceLine().Match(line.Trim());
            if (!match.Success) continue;

            var serial = match.Groups["serial"].Value;
            var state = match.Groups["state"].Value;
            var metadata = match.Groups["meta"].Value;
            var isNetwork = serial.Contains(':');
            var model = ReadMeta(metadata, "model") ?? ReadMeta(metadata, "device") ?? "ADB-устройство";
            var platform = DevicePlatform.AndroidFamily;
            var confidence = DetectionConfidence.Probable;
            var details = state == "device"
                ? "ADB-устройство доступно. Для определения точной ОС требуется запрос свойств устройства."
                : "Устройство отображается в ADB, но не авторизовано или недоступно.";

            if (state == "device")
            {
                var props = await ProcessRunner.RunAsync(adb, ["-s", serial, "shell", "getprop"],
                    TimeSpan.FromSeconds(4), token);
                if (props is { Succeeded: true })
                {
                    var text = props.StandardOutput;
                    var manufacturer = GetProperty(text, "ro.product.manufacturer");
                    var productModel = GetProperty(text, "ro.product.model");
                    var productDevice = GetProperty(text, "ro.product.device");
                    var characteristics = GetProperty(text, "ro.build.characteristics");
                    var fireOs = text.Contains("fire_os", StringComparison.OrdinalIgnoreCase) ||
                                 (manufacturer?.Contains("amazon", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                 (productDevice?.StartsWith("aft", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                 (productModel?.Contains("fire tv", StringComparison.OrdinalIgnoreCase) ?? false);

                    if (fireOs)
                    {
                        platform = DevicePlatform.FireOs;
                        confidence = DetectionConfidence.Confirmed;
                        details = "Подтверждено по системным свойствам Amazon / Fire OS.";
                    }
                    else if ((characteristics?.Contains("tv", StringComparison.OrdinalIgnoreCase) ?? false) ||
                             (productModel?.Contains("tv", StringComparison.OrdinalIgnoreCase) ?? false) ||
                             (productDevice?.Contains("tv", StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        platform = DevicePlatform.AndroidTv;
                        confidence = DetectionConfidence.Probable;
                        details = "Похоже на Android TV. Признак получен из системных свойств; модель следует проверить.";
                    }

                    model = productModel ?? model;
                }
            }

            report.Devices.Add(new TvDevice(
                $"adb:{serial}", model, platform, isNetwork ? ConnectionKind.Network : ConnectionKind.Usb,
                confidence, serial, "ADB devices -l", state == "device" ? "ADB доступен" : $"ADB: {state}", details));
        }

        var mdns = await ProcessRunner.RunAsync(adb, ["mdns", "services"], CommandTimeout, token);
        if (mdns is { Succeeded: true })
        {
            foreach (var line in mdns.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains("_adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("_adb._tcp", StringComparison.OrdinalIgnoreCase)) continue;

                var endpoint = EndpointRegex().Match(line);
                if (!endpoint.Success) continue;
                var ip = endpoint.Groups["ip"].Value;
                var port = endpoint.Groups["port"].Value;
                report.Devices.Add(new TvDevice($"adb-mdns:{ip}:{port}", "Android-устройство (ADB mDNS)",
                    DevicePlatform.AndroidFamily, ConnectionKind.Network, DetectionConfidence.Probable,
                    $"{ip}:{port}", "ADB mDNS service", "Найдено в сети; подключение ещё не подтверждено",
                    "mDNS сообщает о сервисе беспроводной отладки. Для подключения потребуется подтверждение на экране телевизора."));
            }
        }
        else
        {
            report.Notes.Add("ADB mDNS недоступен или эта версия ADB не поддерживает команду mdns services.");
        }
    }

    private static string? ReadMeta(string metadata, string key)
    {
        var match = Regex.Match(metadata, $@"(?:^|\s){Regex.Escape(key)}:([^\s]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? GetProperty(string output, string property)
    {
        var match = Regex.Match(output, $@"\[{Regex.Escape(property)}\]:\s*\[(.*?)\]", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^(?<serial>\S+)\s+(?<state>device|offline|unauthorized|no permissions)(?:\s+(?<meta>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex AdbDeviceLine();

    [GeneratedRegex(@"(?<ip>(?:\d{1,3}\.){3}\d{1,3}):(?<port>\d+)")]
    private static partial Regex EndpointRegex();
}
