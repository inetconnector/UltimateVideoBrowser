using System.Net;
using System.Net.NetworkInformation;
using System.Linq;
using UltimateVideoBrowser.Helpers;

namespace UltimateVideoBrowser.Services;

public sealed record NetworkServerInfo(string DisplayName, string Address);

public sealed class NetworkShareScanner
{
    private const int MaxHosts = 254;
    private const int PingTimeoutMs = 200;
    private const int MaxConcurrency = 24;

    public async Task<IReadOnlyList<NetworkServerInfo>> ScanAsync(CancellationToken ct = default)
    {
        var localIp = GetLocalIpv4Address();
        if (string.IsNullOrWhiteSpace(localIp))
            return Array.Empty<NetworkServerInfo>();

        var prefix = string.Join(".", localIp.Split('.').Take(3));
        if (string.IsNullOrWhiteSpace(prefix))
            return Array.Empty<NetworkServerInfo>();

        var results = new List<NetworkServerInfo>();
        var gate = new SemaphoreSlim(MaxConcurrency);
        var tasks = new List<Task>();

        for (var i = 1; i <= MaxHosts; i++)
        {
            var address = $"{prefix}.{i}";
            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(address, PingTimeoutMs).ConfigureAwait(false);
                    if (reply.Status != IPStatus.Success)
                        return;

                    var hostName = await TryResolveHostNameAsync(address, ct).ConfigureAwait(false);
                    lock (results)
                    {
                        results.Add(new NetworkServerInfo(hostName ?? address, address));
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.LogException(ex, "NetworkShareScanner.ScanAsync", $"Address={address}");
                }
                finally
                {
                    gate.Release();
                }
            }, ct));
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "NetworkShareScanner.ScanAsync", "Task.WhenAll");
        }

        return results
            .OrderBy(result => result.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetLocalIpv4Address()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                var ipProps = nic.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        continue;

                    var ip = addr.Address.ToString();
                    if (IsPrivateNetwork(ip))
                        return ip;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "NetworkShareScanner.GetLocalIpv4Address");
        }

        return null;
    }

    private static bool IsPrivateNetwork(string ip)
    {
        return ip.StartsWith("10.", StringComparison.Ordinal)
               || ip.StartsWith("192.168.", StringComparison.Ordinal)
               || ip.StartsWith("172.16.", StringComparison.Ordinal)
               || ip.StartsWith("172.17.", StringComparison.Ordinal)
               || ip.StartsWith("172.18.", StringComparison.Ordinal)
               || ip.StartsWith("172.19.", StringComparison.Ordinal)
               || ip.StartsWith("172.2", StringComparison.Ordinal)
               || ip.StartsWith("172.3", StringComparison.Ordinal);
    }

    private static async Task<string?> TryResolveHostNameAsync(string address, CancellationToken ct)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(address, ct).ConfigureAwait(false);
            return entry.HostName;
        }
        catch
        {
            return null;
        }
    }
}
