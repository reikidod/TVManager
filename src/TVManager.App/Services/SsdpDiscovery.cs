using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TVManager.Models;

namespace TVManager.Services;

internal sealed class SsdpDiscovery
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), 1900);
    private static readonly string[] SearchTargets =
    [
        "urn:lge-com:service:webos-second-screen:1",
        "ssdp:all"
    ];

    public async Task DiscoverAsync(DiscoveryReport report, CancellationToken token)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.ReceiveTimeout = 500;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);

        foreach (var target in SearchTargets)
        {
            var request = string.Join("\r\n",
                "M-SEARCH * HTTP/1.1",
                "HOST: 239.255.255.250:1900",
                "MAN: \"ssdp:discover\"",
                "MX: 2",
                $"ST: {target}",
                "", "");
            var payload = Encoding.ASCII.GetBytes(request);
            await udp.SendAsync(payload, MulticastEndpoint, token);
        }

        while (DateTimeOffset.UtcNow < deadline && !token.IsCancellationRequested)
        {
            try
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                receiveCts.CancelAfter(remaining > TimeSpan.FromMilliseconds(500)
                    ? TimeSpan.FromMilliseconds(500) : remaining);
                var packet = await udp.ReceiveAsync(receiveCts.Token);
                var response = Encoding.UTF8.GetString(packet.Buffer);
                var headers = ParseHeaders(response);
                if (!headers.TryGetValue("LOCATION", out var location) || !Uri.TryCreate(location, UriKind.Absolute, out var uri))
                    continue;

                var st = headers.GetValueOrDefault("ST", string.Empty);
                var key = $"{uri.Host}|{st}";
                if (!seen.Add(key)) continue;

                var server = headers.GetValueOrDefault("SERVER", string.Empty);
                var info = await ReadDescriptionAsync(uri, token);
                var manufacturer = info.Manufacturer ?? string.Empty;
                var name = info.FriendlyName ?? info.Model ?? "UPnP-устройство";
                var webOsService = st.Contains("webos-second-screen", StringComparison.OrdinalIgnoreCase);
                var isLg = manufacturer.Contains("LG", StringComparison.OrdinalIgnoreCase) ||
                           server.Contains("LG", StringComparison.OrdinalIgnoreCase);
                var isTv = name.Contains("TV", StringComparison.OrdinalIgnoreCase) ||
                           (info.Model?.Contains("TV", StringComparison.OrdinalIgnoreCase) ?? false);

                if (!webOsService && !isLg) continue;

                var platform = isLg && (webOsService || isTv) ? DevicePlatform.LgWebOs : DevicePlatform.Unknown;
                var confidence = webOsService ? DetectionConfidence.Confirmed : DetectionConfidence.Probable;
                var detail = webOsService
                    ? "Найден фирменный SSDP-сервис LG webOS Second Screen."
                    : "Обнаружено LG-устройство по SSDP/UPnP; платформа webOS требует дополнительной проверки.";

                report.Devices.Add(new TvDevice($"ssdp:{uri.Host}", name, platform, ConnectionKind.Network,
                    confidence, uri.Host, $"SSDP: {st}; LOCATION: {uri}", "Обнаружено в локальной сети", detail));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Receive polling slice elapsed; continue until overall discovery deadline.
            }
            catch (OperationCanceledException) { throw; }
            catch (SocketException) { break; }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or FormatException)
            {
                report.Notes.Add($"SSDP: не удалось прочитать описание одного из устройств ({ex.Message}).");
            }
        }
    }

    private static Dictionary<string, string> ParseHeaders(string response)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in response.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static async Task<(string? Manufacturer, string? Model, string? FriendlyName)> ReadDescriptionAsync(Uri uri, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
        try
        {
            using var response = await client.GetAsync(uri, token);
            if (!response.IsSuccessStatusCode) return default;
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, token);
            return (
                FindElement(document, "manufacturer"),
                FindElement(document, "modelName"),
                FindElement(document, "friendlyName"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return default;
        }
    }

    private static string? FindElement(XDocument document, string localName) =>
        document.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
}
