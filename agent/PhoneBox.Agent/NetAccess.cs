using System.Buffers;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Connections;

namespace PhoneBox.Agent;

/// <summary>
/// Fork: which peers may connect to the Wi-Fi port (see <see cref="LanConfig.Access"/>), checked per TCP connection
/// before TLS, and a plain-HTTP redirect so typing "IP:port" in Safari lands on https://IP:port/.
/// </summary>
public static partial class NetAccess
{
    private static (IPAddress Net, int Prefix)[] localNets = [];
    private static long localNetsAt;

    public static bool TryParseCidr(string? text, out IPAddress net, out int prefix)
    {
        net = IPAddress.None; prefix = 0;
        var parts = (text ?? "").Trim().Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out prefix)) return false;
        if (prefix < 0 || prefix > (ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)) return false;
        net = ip;
        return true;
    }

    public static bool InCidr(IPAddress ip, IPAddress net, int prefix)
    {
        var a = ip.GetAddressBytes();
        var b = net.GetAddressBytes();
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length && prefix > 0; i++, prefix -= 8)
        {
            var mask = prefix >= 8 ? 0xFF : (byte)(0xFF << (8 - prefix));
            if ((a[i] & mask) != (b[i] & mask)) return false;
        }
        return true;
    }

    public static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length == 4) return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
        // IPv6 unique-local fc00::/7 and link-local fe80::/10 (phones on the same Wi-Fi often use these)
        return b.Length == 16 && ((b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80));
    }

    /// <summary>The IPv4 subnets of this computer's own adapters (Ethernet, Wi-Fi), refreshed every 30 seconds.</summary>
    public static (IPAddress Net, int Prefix)[] LocalSubnets()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref localNetsAt) < 30_000 && localNets.Length > 0) return localNets;
        var list = new List<(IPAddress, int)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue; // link-local: VPN / Bluetooth leftovers
                    if (ua.PrefixLength is < 8 or > 32) continue;
                    list.Add((ua.Address, ua.PrefixLength));
                }
            }
        }
        catch { }
        localNets = [.. list];
        Interlocked.Exchange(ref localNetsAt, now);
        return localNets;
    }

    public static bool Allowed(IPAddress? ip, string access, IReadOnlyList<string> cidrs, IReadOnlyList<(IPAddress Net, int Prefix)>? subnets = null)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || Tailscale.IsTailscale(ip)) return true;
        if (access == "any") return true;
        if (access != "local") return false;
        if (IsPrivate(ip)) return true;
        foreach (var (net, prefix) in subnets ?? LocalSubnets())
            if (InCidr(ip, net, prefix)) return true;
        foreach (var c in cidrs)
            if (TryParseCidr(c, out var net, out var prefix) && InCidr(ip, net, prefix)) return true;
        return false;
    }

    public static bool Allowed(EndPoint? remote, LanConfig lan) =>
        remote is IPEndPoint ep && Allowed(ep.Address, lan.Access, lan.AllowedCidrs);

    // ------------------------------------------------------------------ plain HTTP on the HTTPS port

    [GeneratedRegex(@"^Host:[ \t]*([A-Za-z0-9.\-]+|\[[0-9A-Fa-f:.]+\])(:\d{1,5})?[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex HostHeader();
    [GeneratedRegex(@"^[A-Z]+ (/[A-Za-z0-9/_.\-]*)")]
    private static partial Regex RequestPath();

    /// <summary>The 301 a plain-HTTP request gets: same host and port, https, and only a simple path (no open redirect).</summary>
    public static string RedirectFor(string head, string fallbackHost)
    {
        var host = HostHeader().Match(head) is { Success: true } m ? m.Groups[1].Value + m.Groups[2].Value : fallbackHost;
        var path = RequestPath().Match(head) is { Success: true } p ? p.Groups[1].Value : "/";
        if (path.StartsWith("//")) path = "/";
        return $"HTTP/1.1 301 Moved Permanently\r\nLocation: https://{host}{path}\r\nContent-Length: 0\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
    }

    /// <summary>
    /// Connection middleware placed before UseHttps: a TLS ClientHello (first byte 0x16) goes on to TLS; anything else is
    /// treated as plain HTTP and answered with a redirect to https on the same port.
    /// </summary>
    public static ConnectionDelegate RedirectPlainHttp(ConnectionDelegate next) => async conn =>
    {
        var input = conn.Transport.Input;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var r = await input.ReadAsync(cts.Token);
            var buf = r.Buffer;
            if (buf.IsEmpty) { input.AdvanceTo(buf.End); return; }
            if (buf.FirstSpan[0] == 0x16) { input.AdvanceTo(buf.Start, buf.Start); await next(conn); return; }
            while (true)
            {
                var text = Encoding.ASCII.GetString(buf.Slice(0, Math.Min(buf.Length, 8192)).ToArray());
                if (text.Contains("\r\n\r\n") || buf.Length >= 8192 || r.IsCompleted)
                {
                    input.AdvanceTo(buf.End);
                    var local = conn.LocalEndPoint as IPEndPoint;
                    var fallback = local is null ? "localhost" : (local.Address.IsIPv4MappedToIPv6 ? local.Address.MapToIPv4() : local.Address) + ":" + local.Port;
                    await conn.Transport.Output.WriteAsync(Encoding.ASCII.GetBytes(RedirectFor(text, fallback)), cts.Token);
                    await conn.Transport.Output.CompleteAsync();
                    return;
                }
                input.AdvanceTo(buf.Start, buf.End);
                r = await input.ReadAsync(cts.Token);
                buf = r.Buffer;
            }
        }
        catch (OperationCanceledException) { conn.Abort(); }
    };
}

/// <summary>
/// Fork: guesses at the web panel PIN. Five wrong PINs lock that address for five minutes; thirty wrong PINs from
/// anywhere within ten minutes lock the PIN for everyone until the window passes. A 6-digit PIN cannot be guessed this way.
/// </summary>
public sealed class PinGate
{
    private readonly object gate = new();
    private readonly Dictionary<string, (int Fails, long Until)> perAddress = [];
    private readonly Queue<long> recent = new();
    public const int PerAddress = 5, Global = 30;
    public const long LockMs = 5 * 60_000, WindowMs = 10 * 60_000;

    public bool Locked(string address, long now)
    {
        lock (gate)
        {
            while (recent.Count > 0 && now - recent.Peek() > WindowMs) recent.Dequeue();
            if (recent.Count >= Global) return true;
            return perAddress.TryGetValue(address, out var s) && s.Until > now;
        }
    }

    public void Fail(string address, long now)
    {
        lock (gate)
        {
            recent.Enqueue(now);
            // after a lock the count restarts at 0 (the lock entry stores 0 fails)
            var fails = (perAddress.TryGetValue(address, out var s) ? s.Fails : 0) + 1;
            perAddress[address] = fails >= PerAddress ? (0, now + LockMs) : (fails, 0);
            if (perAddress.Count > 10_000) perAddress.Clear();
        }
    }

    public void Success(string address) { lock (gate) perAddress.Remove(address); }

    public static bool Matches(string? given, string pin) =>
        given is { Length: 6 } && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(pin));
}
