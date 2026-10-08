using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests;

/// <summary>US1: email/password sign-in, generic failures, explicit logout, and no automatic expiry.</summary>
public sealed class SignInAndSignOutTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact]
    public async Task Valid_credentials_create_a_protected_persistent_session()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();

        var response = await client.SignInAsync(email);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await PortalClient.ReadJsonAsync(response);
        Assert.Equal([PortalRoles.Student], body.GetProperty("availableRoles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(PortalRoles.Student, body.GetProperty("activeRole").GetString());

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(PortalClient.SessionCookieName + "="));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await client.GetCurrentSessionAsync()).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_fail_identically_without_a_session()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var wrongPasswordClient = factory.CreatePortalClient();
        using var unknownEmailClient = factory.CreatePortalClient();

        var wrongPassword = await wrongPasswordClient.SignInAsync(email, "Wrong-pass1");
        var unknownEmail = await unknownEmailClient.SignInAsync(PortalApiFactory.UniqueEmail("nobody"), "Wrong-pass1");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var first = await PortalClient.ReadJsonAsync(wrongPassword);
        var second = await PortalClient.ReadJsonAsync(unknownEmail);
        Assert.Equal(first.GetProperty("title").GetString(), second.GetProperty("title").GetString());
        Assert.Equal(first.GetProperty("detail").GetString(), second.GetProperty("detail").GetString());

        Assert.Null(wrongPasswordClient.SessionCookie);
        Assert.Null(unknownEmailClient.SessionCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongPasswordClient.GetCurrentSessionAsync()).StatusCode);
    }

    [Fact]
    public async Task Sign_in_requires_an_antiforgery_token()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        await factory.CreateUserAsync(email, PortalRoles.Admin);
        using var client = factory.CreatePortalClient();

        var response = await client.SendAsync(
            HttpMethod.Post, "/api/auth/sessions", new { email, password = PortalApiFactory.Password }, withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(client.SessionCookie);
    }

    [Fact]
    public async Task Sign_in_validates_required_fields_and_rejects_unknown_members()
    {
        using var client = factory.CreatePortalClient();

        var missing = await client.SendAsync(HttpMethod.Post, "/api/auth/sessions", new { email = "", password = "" });
        var extra = await client.SendAsync(HttpMethod.Post, "/api/auth/sessions", new { email = "a@iit.test", password = "x", role = "Admin" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var errors = (await PortalClient.ReadJsonAsync(missing)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("Email", out _));
        Assert.True(errors.TryGetProperty("Password", out _));
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
    }

    [Fact]
    public async Task Sign_out_revokes_only_the_current_session_and_its_credential()
    {
        var email = PortalApiFactory.UniqueEmail("coordinator");
        await factory.CreateUserAsync(email, PortalRoles.Coordinator);
        using var first = factory.CreatePortalClient();
        using var second = factory.CreatePortalClient();
        await first.SignInSuccessfullyAsync(email);
        await second.SignInSuccessfullyAsync(email);
        var copiedCredential = first.SessionCookie!;

        Assert.Equal(HttpStatusCode.NoContent, (await first.SignOutAsync()).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetCurrentSessionAsync()).StatusCode);

        using var replay = factory.CreatePortalClient();
        replay.UseSessionCookie(copiedCredential);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/test-probe/modules/coordinator")).StatusCode);
    }

    [Fact]
    public async Task Sessions_do_not_expire_with_age_or_inactivity()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        // Simulate a session created long ago and idle since.
        await factory.WithDbAsync(db => db.AuthSessions
            .Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddYears(-3))));

        Assert.Equal(HttpStatusCode.OK, (await client.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/student")).StatusCode);
    }

    [Fact]
    public async Task Protected_requests_without_a_session_get_a_problem_response()
    {
        using var client = factory.CreatePortalClient();

        var response = await client.GetCurrentSessionAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Sign_out_requires_an_antiforgery_token()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        await factory.CreateUserAsync(email, PortalRoles.Admin);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        var response = await client.SendAsync(HttpMethod.Delete, "/api/auth/sessions/current", withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetCurrentSessionAsync()).StatusCode);
    }
}
