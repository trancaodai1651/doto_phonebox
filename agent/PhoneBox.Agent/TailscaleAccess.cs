using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PhoneBox.Agent;

/// <summary>Tailscale helpers (fork). Who may connect is decided in <see cref="NetAccess"/>.</summary>
public static class Tailscale
{
    public static bool IsTailscale(IPAddress? ip)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 4) return b[0] == 100 && b[1] is >= 64 and <= 127;
        // Tailscale ULA: fd7a:115c:a1e0::/48
        return b.Length == 16 && b[0] == 0xfd && b[1] == 0x7a && b[2] == 0x11 && b[3] == 0x5c && b[4] == 0xa1 && b[5] == 0xe0;
    }

    public static string? Exe()
    {
        var found = ToolProcess.Find("tailscale");
        if (found is not null) return found;
        string[] known = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe")]
            : OperatingSystem.IsMacOS() ? ["/Applications/Tailscale.app/Contents/MacOS/Tailscale"] : ["/usr/bin/tailscale", "/usr/sbin/tailscale"];
        return known.FirstOrDefault(File.Exists);
    }

    /// <summary>This computer's MagicDNS name ("pc.tailnet.ts.net"), or null when Tailscale is not running.</summary>
    public static string? DnsName()
    {
        var exe = Exe();
        if (exe is null) return null;
        try
        {
            var start = new ProcessStartInfo(exe) { ArgumentList = { "status", "--json", "--self", "--peers=false" } };
            var r = ToolProcess.RunAsync(start, TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            if (r.ExitCode != 0) return null;
            using var doc = JsonDocument.Parse(r.Output);
            var name = doc.RootElement.GetProperty("Self").GetProperty("DNSName").GetString()?.TrimEnd('.');
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }
}

/// <summary>
/// A browser-trusted certificate for the MagicDNS name from "tailscale cert" (needs HTTPS enabled in the tailnet's
/// DNS settings). Served only to clients that ask for that name (SNI), so the Android app, which connects by IP and pins
/// the self-signed certificate, keeps working. Renewed daily in the background.
/// </summary>
public sealed class TailscaleCert : IDisposable
{
    private readonly string certPath = Path.Combine(AppPaths.ConfigDir, "tailscale-cert.pem");
    private readonly string keyPath = Path.Combine(AppPaths.ConfigDir, "tailscale-key.pem");
    private readonly System.Threading.Timer timer;
    private X509Certificate2? current;
    private DateTime loadedStamp;

    public string? Name { get; private set; }
    public string? LastError { get; private set; }

    public TailscaleCert()
    {
        // startup only waits for the name and the certificate already on disk; "tailscale cert" runs in the background
        Name = Tailscale.DnsName();
        Load();
        _ = Task.Run(Refresh);
        timer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.FromHours(24), TimeSpan.FromHours(24));
    }

    public X509Certificate2? For(string? sni) =>
        current is not null && Name is not null && string.Equals(sni, Name, StringComparison.OrdinalIgnoreCase) ? current : null;

    public void Refresh()
    {
        try
        {
            Name ??= Tailscale.DnsName();
            var exe = Tailscale.Exe();
            if (Name is null || exe is null) { LastError = "Tailscale is not running"; Load(); return; }
            Directory.CreateDirectory(AppPaths.ConfigDir);
            var start = new ProcessStartInfo(exe) { ArgumentList = { "cert", "--cert-file", certPath, "--key-file", keyPath, Name } };
            var r = ToolProcess.RunAsync(start, TimeSpan.FromSeconds(90), CancellationToken.None).GetAwaiter().GetResult();
            LastError = r.ExitCode == 0 ? null : (r.Error + r.Output).Trim();
            Load();
        }
        catch (Exception e) { LastError = e.Message; Load(); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(certPath) || !File.Exists(keyPath)) return;
            var stamp = File.GetLastWriteTimeUtc(certPath);
            if (current is not null && stamp == loadedStamp) return;
            using var pem = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            // Windows TLS (SChannel) needs a persisted key: round-trip through PKCS#12.
            current = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
            loadedStamp = stamp;
        }
        catch (Exception e) { LastError = e.Message; }
    }

    public void Dispose() => timer.Dispose();
}

/// <summary>
/// The panel as a web page for iPhone / iPad (or any browser). The device types https://IP:port and enters the PIN once
/// (or opens the private link /web/&lt;webToken&gt;); that sets a Secure, HttpOnly, SameSite=Strict cookie and offers "Add to Home Screen". The cookie then
/// opens the panel files and the same phone API the Android app uses, from the panel's own origin only.
/// </summary>
public static class WebPanel
{
    public const string Cookie = "pb_web";

    /// <summary>The pages a browser may open without the cookie: "/" (PIN form), the PIN post, and the private link.</summary>
    public static bool IsLogin(string method, PathString path) =>
        (method == "GET" && (path == "/" || path.StartsWithSegments("/web")))
        || (method == "POST" && path == "/web/login");

    public static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);

    /// <summary>Typing https://IP:port opens this: one PIN, once per device (shown on the computer at /web-setup).</summary>
    public static string LoginHtml(string? error) => """
        <!doctype html><html lang="vi"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
        <meta name="apple-mobile-web-app-capable" content="yes">
        <meta name="mobile-web-app-capable" content="yes">
        <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
        <meta name="apple-mobile-web-app-title" content="PhoneBox">
        <meta name="theme-color" content="#0b0d12">
        <title>PhoneBox</title>
        <style>body{margin:0;background:#0b0d12;color:#e8eaf0;font:16px/1.5 -apple-system,system-ui,sans-serif;padding:max(32px,env(safe-area-inset-top)) 20px 24px}
        main{max-width:360px;margin:auto;text-align:center}h1{font-size:26px}
        input{width:100%;box-sizing:border-box;font-size:32px;letter-spacing:10px;text-align:center;padding:12px;border-radius:12px;border:1px solid #333a4a;background:#151924;color:#fff}
        button{width:100%;margin-top:16px;background:#4f7cff;color:#fff;border:0;padding:14px;border-radius:12px;font-size:17px;font-weight:600}
        .err{color:#ff8a8a}small{color:#9aa1b2}</style></head><body><main>
        <h1>PhoneBox</h1><p>Nhập mã PIN hiển thị trên máy tính<br><small>(trang 127.0.0.1:8765/web-setup)</small></p>
        <form method="post" action="/web/login">
        <input name="pin" inputmode="numeric" pattern="[0-9]{6}" maxlength="6" autocomplete="one-time-code" autofocus required>
        <button type="submit">Mở bảng điều khiển</button></form>
        """ + (error is null ? "" : $"<p class=\"err\">{Esc(error)}</p>") + "</main></body></html>";

    /// <summary>Files of the panel itself (the API is checked by <see cref="Lan.AllowedOverLan"/>).</summary>
    public static bool IsPage(PathString path) => path.StartsWithSegments("/panel") || path == "/favicon.ico";

    public static bool CookieMatches(HttpRequest req, string webToken) =>
        Lan.MatchesQuery(req.Cookies[Cookie], webToken);

    /// <summary>
    /// The cookie only counts for requests made by the panel's own pages: no cross-site fetch (sandboxed Custom HTML
    /// widgets have an opaque origin, so their requests are cross-site), and changes need the panel's header too.
    /// </summary>
    public static bool FromPanel(HttpRequest req)
    {
        var site = req.Headers["Sec-Fetch-Site"].ToString();
        if (site is not ("" or "same-origin" or "none")) return false;
        var origin = req.Headers.Origin.ToString();
        if (origin != "" && origin != $"https://{req.Host}") return false;
        return HttpMethods.IsGet(req.Method) || HttpMethods.IsHead(req.Method) || req.Headers.ContainsKey("X-PhoneBox-Panel");
    }

    public static void SetCookie(HttpResponse res, string webToken) =>
        res.Cookies.Append(Cookie, webToken, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(400), IsEssential = true,
        });

    /// <summary>The landing page: in Safari it explains "Add to Home Screen"; launched from the Home Screen it opens the panel.</summary>
    public const string LandingHtml = """
        <!doctype html><html lang="vi"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
        <meta name="apple-mobile-web-app-capable" content="yes">
        <meta name="mobile-web-app-capable" content="yes">
        <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
        <meta name="apple-mobile-web-app-title" content="PhoneBox">
        <meta name="theme-color" content="#0b0d12">
        <title>PhoneBox</title>
        <style>body{margin:0;background:#0b0d12;color:#e8eaf0;font:16px/1.5 -apple-system,system-ui,sans-serif;padding:max(24px,env(safe-area-inset-top)) 20px 24px}
        main{max-width:520px;margin:auto}h1{font-size:24px}a.btn{display:block;text-align:center;background:#4f7cff;color:#fff;padding:14px;border-radius:12px;text-decoration:none;font-weight:600;margin:24px 0}
        ol{padding-left:20px}small{color:#9aa1b2}</style></head><body><main>
        <h1>PhoneBox</h1>
        <p>Thiết bị này đã được cấp quyền mở bảng điều khiển.</p>
        <p><b>Để dùng toàn màn hình như một ứng dụng:</b></p>
        <ol><li>Nhấn nút <b>Chia sẻ</b> (ô vuông có mũi tên) trong Safari.</li><li>Chọn <b>Thêm vào MH chính</b> (Add to Home Screen).</li><li>Mở PhoneBox từ biểu tượng mới trên màn hình chính.</li></ol>
        <a class="btn" href="/panel/">Mở bảng điều khiển</a>
        <small>Thiết bị này sẽ được ghi nhớ. Trên thiết bị khác, gõ lại IP:cổng và nhập PIN.</small>
        </main><script>
        if (navigator.standalone || matchMedia('(display-mode: standalone)').matches) location.replace('/panel/');
        </script></body></html>
        """;
}

/// <summary>Local page (http://127.0.0.1:8765/web-setup) with the iPhone / iPad link and its QR code.</summary>
public static class WebSetupPage
{
    public const string Html = """
        <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>PhoneBox – iPhone / iPad</title>
        <style>body{font:15px/1.5 system-ui,sans-serif;background:#0f1117;color:#e6e8ee;margin:0;padding:32px}main{max-width:720px;margin:auto}
        img{background:#fff;padding:12px;border-radius:12px;width:260px;height:260px}code{background:#1b1f2a;padding:2px 6px;border-radius:6px;word-break:break-all}
        .warn{background:#3a2a10;border:1px solid #8a6420;padding:12px;border-radius:10px}.ok{background:#12301e;border:1px solid #2f7a4b;padding:12px;border-radius:10px}
        li{margin:6px 0}a{color:#8fb0ff}</style></head><body><main>
        <h1>PhoneBox trên iPhone / iPad</h1><div id="out">Đang tải…</div></main>
        <script>
        const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
        fetch('/api/web', { cache: 'no-store' }).then((r) => r.json()).then((d) => {
          const out = document.getElementById('out');
          if (!d.enabled) { out.innerHTML = '<p class="warn">Chưa bật. Trong Cài đặt, bật Wi-Fi (lan.enabled) và lan.webPanel, rồi khởi động lại agent.</p>'; return; }
          let h = '';
          h += d.trustedName
            ? `<p class="ok">Chứng chỉ Tailscale hợp lệ cho <b>${esc(d.trustedName)}</b>: Safari sẽ không cảnh báo.</p>`
            : `<p class="warn">Chưa có chứng chỉ Tailscale, Safari sẽ cảnh báo chứng chỉ tự ký. Bật <b>HTTPS Certificates</b> trong trang quản trị Tailscale (DNS), rồi khởi động lại agent.${d.certError ? '<br><small>' + esc(d.certError) + '</small>' : ''}</p>`;
          const net = { tailscale: "Chỉ Tailscale", local: "Tailscale + mạng nội bộ (Ethernet / Wi-Fi cùng mạng)", any: "Mọi mạng" }[d.access] || d.access;
          h += `<h2>Cách 1: gõ địa chỉ trong Safari</h2><p>Trên iPhone / iPad, gõ một trong các địa chỉ sau vào thanh địa chỉ Safari:</p><ul>`
            + d.addresses.map((a) => `<li><code style="font-size:18px">${esc(a)}</code></li>`).join("") + "</ul>"
            + `<p>Rồi nhập mã PIN: <b style="font-size:28px;letter-spacing:6px">${esc(d.pin)}</b></p>`
            + "<p><small>Safari sẽ cảnh báo chứng chỉ tự ký ở lần đầu: chọn <b>Hiển thị chi tiết → truy cập trang web này</b>.</small></p>";
          h += `<p>Mạng được phép: <b>${esc(net)}</b>${d.allowedCidrs && d.allowedCidrs.length ? " + " + d.allowedCidrs.map(esc).join(", ") : ""}</p>`;
          if (d.qr) h += `<h2>Cách 2: quét QR (không cần PIN)</h2><img alt="QR" src="data:image/svg+xml;utf8,${encodeURIComponent(d.qr)}">`;
          h += "<p>Sau đó: Chia sẻ → <b>Thêm vào MH chính</b>. Đổi PIN: xoá <code>lan.webPin</code>; thu hồi mọi trình duyệt đã đăng nhập: xoá <code>lan.webToken</code> trong config.json, rồi khởi động lại agent.</p>";
          out.innerHTML = h;
        }).catch((e) => { document.getElementById('out').textContent = 'Lỗi: ' + e; });
        </script></body></html>
        """;
}
