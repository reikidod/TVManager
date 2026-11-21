using TVManager.Models;

namespace TVManager.Services;

public sealed class DiscoveryService
{
    private readonly AdbDiscovery _adb = new();
    private readonly SdbDiscovery _sdb = new();
    private readonly SsdpDiscovery _ssdp = new();
    private readonly UsbDiscovery _usb = new();
    private readonly NetworkDiscovery _network = new();

    public async Task<DiscoveryReport> ScanAsync(CancellationToken token)
    {
        var started = DateTimeOffset.Now;
        var providers = new Func<CancellationToken, Task<DiscoveryReport>>[]
        {
            ct => RunProviderAsync((r, c) => _adb.DiscoverAsync(r, c), ct),
            ct => RunProviderAsync((r, c) => _sdb.DiscoverAsync(r, c), ct),
            ct => RunProviderAsync((r, c) => _ssdp.DiscoverAsync(r, c), ct),
            ct => RunProviderAsync((r, c) => _network.DiscoverAsync(r, c), ct),
            ct => RunProviderAsync((r, c) => _usb.DiscoverAsync(r, c), ct)
        };

        var tasks = providers.Select(provider => provider(token));
        var results = await Task.WhenAll(tasks);
        var combined = new DiscoveryReport { StartedAt = started };
        foreach (var result in results)
        {
            combined.Devices.AddRange(result.Devices);
            combined.Notes.AddRange(result.Notes);
        }

        combined.FinishedAt = DateTimeOffset.Now;
        return Deduplicate(combined);
    }

    private static async Task<DiscoveryReport> RunProviderAsync(
        Func<DiscoveryReport, CancellationToken, Task> action, CancellationToken token)
    {
        var report = new DiscoveryReport { StartedAt = DateTimeOffset.Now };
        try
        {
            await action(report, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            report.Notes.Add($"Один из модулей обнаружения завершился с ошибкой: {ex.Message}");
        }
        report.FinishedAt = DateTimeOffset.Now;
        return report;
    }

    private static DiscoveryReport Deduplicate(DiscoveryReport report)
    {
        var byAddress = new Dictionary<string, TvDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in report.Devices)
        {
            var key = $"{device.Connection}:{device.Address}";
            if (!byAddress.TryGetValue(key, out var existing) || (int)device.Confidence > (int)existing.Confidence)
                byAddress[key] = device;
        }

        var deduplicated = new DiscoveryReport { StartedAt = report.StartedAt, FinishedAt = report.FinishedAt };
        deduplicated.Devices.AddRange(byAddress.Values.OrderByDescending(x => x.Confidence).ThenBy(x => x.Name));
        deduplicated.Notes.AddRange(report.Notes.Distinct(StringComparer.OrdinalIgnoreCase));
        return deduplicated;
    }
}
