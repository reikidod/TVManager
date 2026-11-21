using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TVManager.Models;

namespace TVManager.Services;

internal sealed class NetworkDiscovery
{
    private const int AdbLegacyPort = 5555;
    private const int TizenSdbPort = 26101;
    private const int MaxConcurrentProbes = 64;

    public async Task DiscoverAsync(DiscoveryReport report, CancellationToken token)
    {
        var addresses = GetScanAddresses();
        if (addresses.Count == 0)
        {
            report.Notes.Add("Не найден подходящий локальный IPv4-интерфейс для поиска в сети.");
            return;
        }

        report.Notes.Add($"Проверяются известные диагностические порты в {addresses.Count} адресах локальной сети. Поиск ограничен локальными /24-подсетями.");
        using var gate = new SemaphoreSlim(MaxConcurrentProbes);
        var resultLock = new object();
        var tasks = addresses.Select(async ip =>
        {
            await gate.WaitAsync(token);
            try
            {
                var adbOpen = await IsPortOpenAsync(ip, AdbLegacyPort, token);
                var sdbOpen = await IsPortOpenAsync(ip, TizenSdbPort, token);

                if (adbOpen)
                {
                    lock (resultLock)
                    {
                        report.Devices.Add(new TvDevice($"tcp-adb:{ip}:{AdbLegacyPort}", "ADB endpoint (тип устройства не проверен)",
                            DevicePlatform.AndroidFamily, ConnectionKind.Network, DetectionConfidence.Low,
                            $"{ip}:{AdbLegacyPort}", "TCP probe 5555", "ADB-порт отвечает; авторизация не проверялась",
                            "Открытый ADB-порт не доказывает, что это телевизор. TV Manager не подключается автоматически: подтверждение выполняется только после выбора устройства."));
                    }
                }

                if (sdbOpen)
                {
                    lock (resultLock)
                    {
                        report.Devices.Add(new TvDevice($"tcp-sdb:{ip}:{TizenSdbPort}", "SDB endpoint (вероятно Tizen)",
                            DevicePlatform.SamsungTizen, ConnectionKind.Network, DetectionConfidence.Probable,
                            $"{ip}:{TizenSdbPort}", "TCP probe 26101", "Порт SDB отвечает; устройство ещё не авторизовано",
                            "Порт Smart Development Bridge типичен для Tizen Developer Mode, но сам по себе не подтверждает модель телевизора."));
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress address, int port, CancellationToken token)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(450));
        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            if (token.IsCancellationRequested) throw;
            return false;
        }
    }

    private static List<IPAddress> GetScanAddresses()
    {
        var result = new HashSet<IPAddress>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            IPInterfaceProperties properties;
            try { properties = adapter.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            foreach (var unicast in properties.UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip) || ip.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    continue;

                var prefix = unicast.PrefixLength;
                if (prefix < 16 || prefix > 30) continue;

                // Never sweep an entire /16 or larger. For broad subnets, inspect the local /24 segment only.
                var bytes = ip.GetAddressBytes();
                uint current = ToUInt32(bytes);
                uint mask = prefix <= 24 ? 0xFFFFFF00u : uint.MaxValue << (32 - prefix);
                uint network = current & mask;
                uint broadcast = network | ~mask;
                if (prefix < 24)
                {
                    network = current & 0xFFFFFF00u;
                    broadcast = network | 0xFFu;
                }

                for (uint host = network + 1; host < broadcast; host++)
                    result.Add(FromUInt32(host));
            }
        }

        return result.ToList();
    }

    private static uint ToUInt32(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static IPAddress FromUInt32(uint value) => new IPAddress(new byte[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    });
}
