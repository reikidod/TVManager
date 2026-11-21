using System.Text.RegularExpressions;
using TVManager.Models;

namespace TVManager.Services;

internal sealed partial class SdbDiscovery
{
    public async Task DiscoverAsync(DiscoveryReport report, CancellationToken token)
    {
        var sdb = ToolLocator.FindOnPath("sdb");
        if (sdb is null)
        {
            report.Notes.Add("SDB не найден. Установите Tizen Studio и TV SDK Tools с сайта Samsung; TV Manager проверяет стандартные каталоги Tizen Studio, TIZEN_STUDIO и PATH.");
            return;
        }

        var result = await ProcessRunner.RunAsync(sdb, ["devices"], TimeSpan.FromSeconds(5), token);
        if (result is null || result.TimedOut)
        {
            report.Notes.Add("Не удалось запросить список устройств SDB.");
            return;
        }

        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = SdbLine().Match(line.Trim());
            if (!match.Success) continue;
            var serial = match.Groups["serial"].Value;
            var state = match.Groups["state"].Value;
            var label = match.Groups["name"].Value.Trim();
            var isNetwork = serial.Contains(':') || Regex.IsMatch(serial, @"^\d{1,3}(\.\d{1,3}){3}$");

            report.Devices.Add(new TvDevice($"sdb:{serial}", string.IsNullOrWhiteSpace(label) ? "Samsung / Tizen device" : label,
                DevicePlatform.SamsungTizen, isNetwork ? ConnectionKind.Network : ConnectionKind.Usb,
                state.Equals("device", StringComparison.OrdinalIgnoreCase) ? DetectionConfidence.Confirmed : DetectionConfidence.Probable,
                serial, "sdb devices", state.Equals("device", StringComparison.OrdinalIgnoreCase) ? "SDB доступен" : $"SDB: {state}",
                "Обнаружено через Smart Development Bridge. Для телевизора Samsung потребуется включённый Developer Mode; SDB может также видеть другие Tizen-устройства."));
        }
    }

    [GeneratedRegex(@"^(?<serial>\S+)\s+(?<state>device|offline)(?:\s+(?<name>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex SdbLine();
}
