using System;
using System.Linq;
using NHibernaut.Server.Host;
using Xunit;

namespace NHibernaut.Tests.Host;

public class DashboardHostOptionsTests
{
    private static Func<string, string?> Env(params (string Key, string? Value)[] kv)
        => key => kv.FirstOrDefault(p => p.Key == key).Value;

    [Fact]
    public void Defaults_to_all_interfaces_port_5005_and_generates_token()
    {
        var o = DashboardHostOptions.Resolve(Env(), () => "GEN");
        Assert.Equal("0.0.0.0", o.BindAddress);
        Assert.Equal(5005, o.Port);
        Assert.Equal("GEN", o.AuthToken);
        Assert.True(o.TokenWasGenerated);
    }

    [Fact]
    public void Loopback_bind_requires_no_token()
    {
        var o = DashboardHostOptions.Resolve(Env(("NHIBERNAUT_BIND", "127.0.0.1")), () => "GEN");
        Assert.Null(o.AuthToken);
        Assert.False(o.TokenWasGenerated);
    }

    [Fact]
    public void Explicit_token_used_verbatim_on_non_loopback()
    {
        var o = DashboardHostOptions.Resolve(
            Env(("NHIBERNAUT_BIND", "0.0.0.0"), ("NHIBERNAUT_AUTH_TOKEN", "secret")), () => "GEN");
        Assert.Equal("secret", o.AuthToken);
        Assert.False(o.TokenWasGenerated);
    }

    [Fact]
    public void Custom_port_is_parsed()
    {
        var o = DashboardHostOptions.Resolve(Env(("NHIBERNAUT_PORT", "8080")), () => "GEN");
        Assert.Equal(8080, o.Port);
    }

    [Fact]
    public void Invalid_port_throws()
        => Assert.Throws<FormatException>(
            () => DashboardHostOptions.Resolve(Env(("NHIBERNAUT_PORT", "abc")), () => "GEN"));

    [Fact]
    public void Generated_token_is_url_safe()
    {
        var token = DashboardHostOptions.GenerateToken();
        Assert.False(string.IsNullOrEmpty(token));
        // Must survive ?token= unchanged: no Base64 '+', '/', '=' that query parsing would mangle.
        Assert.Equal(token, Uri.EscapeDataString(token));
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }
}
