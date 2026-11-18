using System.Net;
using Microsoft.AspNetCore.Http;
using PhoneBox.Agent;
using Xunit;

/// <summary>Fork: Tailscale-only Wi-Fi port and the iPhone / iPad web panel.</summary>
public class TailscaleAccessTests
{
    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.100.10.20", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("192.168.1.20", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("fd7a:115c:a1e0::b42f:729", true)]
    [InlineData("fd7a:115c:a1e1::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:100.70.1.2", true)]
    [InlineData("::ffff:192.168.1.2", false)]
    public void OnlyTailscaleAddressesCount(string ip, bool expected) => Assert.Equal(expected, Tailscale.IsTailscale(IPAddress.Parse(ip)));

    private static readonly (IPAddress, int)[] Subnets = [(IPAddress.Parse("192.0.2.45"), 24)];

    [Theory]
    [InlineData("127.0.0.1", "tailscale", true)]
    [InlineData("::1", "tailscale", true)]
    [InlineData("100.100.1.1", "tailscale", true)]
    [InlineData("192.168.0.9", "tailscale", false)]
    [InlineData("192.0.2.99", "tailscale", false)]
    [InlineData("192.0.2.99", "local", true)]   // same Ethernet subnet
    [InlineData("::ffff:192.0.2.7", "local", true)]
    [InlineData("192.0.3.99", "local", false)]  // another subnet
    [InlineData("192.168.1.20", "local", true)]
    [InlineData("10.1.2.3", "local", true)]
    [InlineData("fe80::1234", "local", true)]
    [InlineData("8.8.8.8", "local", false)]
    [InlineData("8.8.8.8", "any", true)]
    [InlineData("100.70.0.1", "local", true)]
    public void AccessLevels(string ip, string access, bool expected) =>
        Assert.Equal(expected, NetAccess.Allowed(IPAddress.Parse(ip), access, [], Subnets));

    [Fact]
    public void ExtraNetworksCanBeAllowed()
    {
        Assert.True(NetAccess.Allowed(IPAddress.Parse("203.0.113.9"), "local", ["203.0.113.0/24"], Subnets));
        Assert.False(NetAccess.Allowed(IPAddress.Parse("203.0.114.9"), "local", ["203.0.113.0/24"], Subnets));
    }

    [Theory]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("fd00::/8", true)]
    [InlineData("10.0.0.0/33", false)]
    [InlineData("10.0.0.0", false)]
    [InlineData("nope/8", false)]
    public void CidrsAreParsed(string cidr, bool ok) => Assert.Equal(ok, NetAccess.TryParseCidr(cidr, out _, out _));

    [Fact]
    public void PlainHttpIsSentToHttpsOnTheSamePort()
    {
        var r = NetAccess.RedirectFor("GET / HTTP/1.1\r\nHost: 192.0.2.45:8766\r\nUser-Agent: x\r\n\r\n", "fallback:1");
        Assert.StartsWith("HTTP/1.1 301", r);
        Assert.Contains("Location: https://192.0.2.45:8766/\r\n", r);
        Assert.Contains("Location: https://[fd7a::1]:8766/panel/\r\n", NetAccess.RedirectFor("GET /panel/ HTTP/1.1\r\nhost: [fd7a::1]:8766\r\n\r\n", "x"));
    }

    [Fact]
    public void RedirectCannotBeTurnedIntoAnOpenRedirectOrHeaderInjection()
    {
        Assert.Contains("Location: https://10.0.0.1:8766/\r\n", NetAccess.RedirectFor("GET //evil.example HTTP/1.1\r\nHost: 10.0.0.1:8766\r\n\r\n", "x"));
        Assert.Contains("Location: https://10.0.0.1:8766/\r\n", NetAccess.RedirectFor("GET / HTTP/1.1\r\nHost: evil.example/@x\r\n\r\n", "10.0.0.1:8766"));
        Assert.DoesNotContain("Set-Cookie", NetAccess.RedirectFor("GET /%0d%0aSet-Cookie:x HTTP/1.1\r\nHost: a\r\n\r\n", "x"));
    }

    [Fact]
    public void FiveWrongPinsLockThatAddress()
    {
        var gate = new PinGate();
        for (var i = 0; i < PinGate.PerAddress; i++) { Assert.False(gate.Locked("a", 1000)); gate.Fail("a", 1000); }
        Assert.True(gate.Locked("a", 1000));
        Assert.False(gate.Locked("b", 1000));
        Assert.False(gate.Locked("a", 1000 + PinGate.LockMs + 1));
    }

    [Fact]
    public void ManyWrongPinsFromAnywhereLockEveryone()
    {
        var gate = new PinGate();
        for (var i = 0; i < PinGate.Global; i++) gate.Fail("ip" + i, 1000);
        Assert.True(gate.Locked("fresh", 1000));
        Assert.False(gate.Locked("fresh", 1000 + PinGate.WindowMs + 1));
    }

    [Theory]
    [InlineData("123456", "123456", true)]
    [InlineData("123457", "123456", false)]
    [InlineData("12345", "123456", false)]
    [InlineData(null, "123456", false)]
    public void PinCompare(string? given, string pin, bool ok) => Assert.Equal(ok, PinGate.Matches(given, pin));

    private static HttpRequest Request(string method, string? site = null, string? origin = null, bool panelHeader = false)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Host = new HostString("pc.tail1234.ts.net", 8766);
        if (site is not null) ctx.Request.Headers["Sec-Fetch-Site"] = site;
        if (origin is not null) ctx.Request.Headers.Origin = origin;
        if (panelHeader) ctx.Request.Headers["X-PhoneBox-Panel"] = "1";
        return ctx.Request;
    }

    [Fact]
    public void PanelGetsFromItsOwnOriginAreAccepted() => Assert.True(WebPanel.FromPanel(Request("GET", "same-origin")));

    [Fact]
    public void CrossSiteRequestsAreRefused()
    {
        Assert.False(WebPanel.FromPanel(Request("GET", "cross-site")));
        Assert.False(WebPanel.FromPanel(Request("POST", "same-site", "https://evil.example", panelHeader: true)));
    }

    [Fact]
    public void ChangesNeedThePanelHeader()
    {
        Assert.False(WebPanel.FromPanel(Request("POST", "same-origin", "https://pc.tail1234.ts.net:8766")));
        Assert.True(WebPanel.FromPanel(Request("POST", "same-origin", "https://pc.tail1234.ts.net:8766", panelHeader: true)));
    }

    [Fact]
    public void ForeignOriginIsRefusedEvenWithTheHeader() =>
        Assert.False(WebPanel.FromPanel(Request("POST", "same-origin", "https://other.ts.net:8766", panelHeader: true)));

    [Theory]
    [InlineData("GET", "/web/abc", true)]
    [InlineData("GET", "/", true)]
    [InlineData("POST", "/web/login", true)]
    [InlineData("POST", "/web/abc", false)]
    [InlineData("POST", "/", false)]
    [InlineData("GET", "/webx", false)]
    public void LoginIsAGetOfTheLink(string method, string path, bool expected) =>
        Assert.Equal(expected, WebPanel.IsLogin(method, new PathString(path)));

    [Fact]
    public void SettingsStayLocalOnly()
    {
        Assert.True(WebPanel.IsPage("/panel/main.js"));
        Assert.False(WebPanel.IsPage("/"));
        Assert.False(WebPanel.IsPage("/web-setup"));
        Assert.False(Lan.AllowedOverLan("/api/web"));
    }

    [Theory]
    [InlineData("""{"lan":{"access":" TailScale ","webPin":"12","allowedCidrs":["10.9.0.0/16","junk"]}}""", "tailscale", 1)]
    [InlineData("""{"lan":{"access":"everything","webPin":"654321"}}""", "local", 0)]
    [InlineData("""{"lan":{"tailscaleOnly":true}}""", "local", 0)]
    public void LanSettingsAreNormalized(string json, string access, int cidrs)
    {
        var path = Path.Combine(Path.GetTempPath(), $"phonebox-lan-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            var store = new ConfigStore(path);
            Assert.Null(store.Save(json));
            var lan = store.Get().Lan;
            Assert.Equal(access, lan.Access);
            Assert.Equal(cidrs, lan.AllowedCidrs.Count);
            Assert.Matches("^[0-9]{6}$", lan.WebPin);
            Assert.False(string.IsNullOrEmpty(lan.WebToken));
            if (json.Contains("654321")) Assert.Equal("654321", lan.WebPin);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [Fact]
    public void ForkDefaults()
    {
        var c = new AgentConfig();
        Assert.Equal("local", c.Lan.Access);
        Assert.False(c.Updates.Auto);
    }
}
