using System.Management;
using TVManager.Models;

namespace TVManager.Services;

internal sealed class UsbDiscovery
{
    public Task DiscoverAsync(DiscoveryReport report, CancellationToken token) => Task.Run(() =>
    {
        if (!OperatingSystem.IsWindows())
        {
            report.Notes.Add("USB-инвентаризация через Windows Plug and Play доступна только в Windows.");
            return;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, Manufacturer, Status FROM Win32_PnPEntity");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                token.ThrowIfCancellationRequested();
                var name = Convert.ToString(item["Name"]) ?? string.Empty;
                var pnpId = Convert.ToString(item["PNPDeviceID"]) ?? string.Empty;
                var manufacturer = Convert.ToString(item["Manufacturer"]) ?? string.Empty;
                var status = Convert.ToString(item["Status"]) ?? "Unknown";
                var combined = $"{name} {manufacturer} {pnpId}";

                if (!ContainsAny(combined, "ADB", "Android", "Samsung", "Tizen", "webOS", "LG Electronics", "Amazon Fire"))
                    continue;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pnpId)) continue;

                // WMI names alone cannot reliably distinguish a TV from a phone, dongle or development board.
                report.Devices.Add(new TvDevice($"pnp:{pnpId}", name, DevicePlatform.Unknown, ConnectionKind.Usb,
                    DetectionConfidence.Low, pnpId, "Windows Plug and Play / Win32_PnPEntity",
                    $"USB/PnP: {status}",
                    "Найден USB-интерфейс с похожим именем. Это ещё не подтверждает, что подключён именно телевизор; требуется проверка через ADB/SDB или модель устройства."));
            }
        }
        catch (ManagementException ex)
        {
            report.Notes.Add($"Windows не предоставила USB-инвентаризацию через WMI: {ex.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            report.Notes.Add("Нет доступа к части сведений Windows Plug and Play. Запустите приложение с обычными правами или проверьте политики системы.");
        }
    }, token);

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
