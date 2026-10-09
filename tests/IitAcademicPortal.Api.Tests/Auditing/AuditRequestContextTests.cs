using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>The server-derived facts on every event: client address and correlation identifier.</summary>
public sealed class AuditRequestContextTests
{
    private const string Forwarded = "198.51.100.7";

    private static async Task<string?> SourceOfFailedSignInAsync(PortalApiFactory factory, string? forwardedFor)
    {
        using var client = factory.CreatePortalClient();
        var headers = forwardedFor is null ? null : new Dictionary<string, string> { ["X-Forwarded-For"] = forwardedFor };
        await client.SendAsync(HttpMethod.Post, "/api/auth/sessions", new { email = "nobody@iit.test", password = "x" }, headers: headers);
        return Assert.Single(await factory.AuditEventsAsync("auth.sign-in")).Source;
    }

    [Fact]
    public async Task Without_trusted_proxies_the_source_is_the_connection_address_and_forwarded_headers_are_ignored()
    {
        using var factory = new PortalApiFactory();

        Assert.Equal(PortalApiFactory.ClientAddress, await SourceOfFailedSignInAsync(factory, Forwarded));
    }

    [Fact]
    public async Task A_forwarded_address_is_used_only_when_it_comes_from_a_configured_trusted_proxy()
    {
        using var trusted = new ProxyFactory(PortalApiFactory.ClientAddress);

        Assert.Equal(Forwarded, await SourceOfFailedSignInAsync(trusted, Forwarded));
    }

    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_proxy_is_ignored()
    {
        using var untrusted = new ProxyFactory("192.0.2.200");

        Assert.Equal(PortalApiFactory.ClientAddress, await SourceOfFailedSignInAsync(untrusted, Forwarded));
    }

    [Fact]
    public async Task The_correlation_identifier_is_generated_by_the_server_for_each_request_and_never_taken_from_the_client()
    {
        using var factory = new PortalApiFactory();
        using var client = factory.CreatePortalClient();
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            ["X-Request-Id"] = "client-chosen-id",
            ["X-Correlation-Id"] = "client-chosen-correlation",
        };

        await client.SendAsync(HttpMethod.Post, "/api/auth/sessions", new { email = "a@iit.test", password = "x" }, headers: headers);
        await client.SendAsync(HttpMethod.Post, "/api/auth/sessions", new { email = "a@iit.test", password = "x" }, headers: headers);

        var events = await factory.AuditEventsAsync("auth.sign-in");
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.False(string.IsNullOrEmpty(e.CorrelationId));
            Assert.DoesNotContain("0af7651916cd43dd8448eb211c80319c", e.CorrelationId);
            Assert.DoesNotContain("client-chosen", e.CorrelationId);
        });
        Assert.NotEqual(events[0].CorrelationId, events[1].CorrelationId);
    }

    private sealed class ProxyFactory(string trustedProxy) : PortalApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ForwardedHeaders:KnownProxies:0", trustedProxy);
        }
    }
}
