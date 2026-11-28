using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace NewsStudio.Infrastructure.Common;

/// <summary>
/// Outbound connections try IPv4 addresses first and give each address at most a few seconds.
/// .NET connects to a host's addresses one by one, IPv6 first, each until the OS gives up: where IPv6 leads nowhere
/// (seen on this PC: IPv6 to generativelanguage.googleapis.com never connects, IPv4 takes 0.04 s) every new connection
/// waited ~3 minutes — Gemini answers looked like time-outs although the model needs 4 s.
/// </summary>
public static class FastConnect
{
    private static readonly TimeSpan PerAddress = TimeSpan.FromSeconds(5);

    public static SocketsHttpHandler CreateHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(30),
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, ct);
        var ordered = addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();
        if (ordered.Count == 0) throw new SocketException((int)SocketError.HostNotFound);

        Exception? last = null;
        foreach (var address in ordered)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(PerAddress);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                socket.Dispose();
                last = ex;
            }
        }
        throw new HttpRequestException($"Không kết nối được tới {host}:{port}.", last);
    }
}
