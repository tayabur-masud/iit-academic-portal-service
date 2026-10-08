using System.Net;
using System.Text.RegularExpressions;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests;

/// <summary>US3: non-enumerating recovery, proof validation, password policy, and reset-session revocation.</summary>
public sealed partial class PasswordRecoveryTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    private const string NewPassword = "Replacement-pass2";

    [Fact]
    public async Task Known_and_unknown_emails_get_the_same_response_and_only_known_accounts_get_email()
    {
        var knownEmail = PortalApiFactory.UniqueEmail("student");
        var unknownEmail = PortalApiFactory.UniqueEmail("nobody");
        await factory.CreateUserAsync(knownEmail, PortalRoles.Student);
        using var client = factory.CreatePortalClient();

        var unknown = await RequestResetAsync(client, unknownEmail);
        var known = await RequestResetAsync(client, knownEmail);

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());

        // Requests are processed in order, so once the known email arrives the unknown one was handled.
        var email = await factory.Emails.WaitForAsync(knownEmail);
        Assert.Contains("https://portal.test/reset-password?email=", email.Body);
        Assert.Empty(factory.Emails.SentTo(unknownEmail));
    }

    [Fact]
    public async Task Valid_proof_replaces_the_password()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();
        var proof = await RequestProofAsync(client, email);

        var reset = await CompleteResetAsync(client, email, proof, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await factory.CreatePortalClient().SignInAsync(email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreatePortalClient().SignInAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Proof_cannot_be_reused()
    {
        var email = PortalApiFactory.UniqueEmail("coordinator");
        await factory.CreateUserAsync(email, PortalRoles.Coordinator);
        using var client = factory.CreatePortalClient();
        var proof = await RequestProofAsync(client, email);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteResetAsync(client, email, proof, NewPassword)).StatusCode);

        var replay = await CompleteResetAsync(client, email, proof, "Another-pass3");

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await factory.CreatePortalClient().SignInAsync(email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Invalid_proof_or_unknown_email_is_rejected_without_changing_the_password()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        await factory.CreateUserAsync(email, PortalRoles.Admin);
        using var client = factory.CreatePortalClient();

        var invalid = await CompleteResetAsync(client, email, "not-a-real-proof", NewPassword);
        var unknown = await CompleteResetAsync(client, PortalApiFactory.UniqueEmail("nobody"), "not-a-real-proof", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(await invalid.Content.ReadAsStringAsync().ContinueWith(t => StripTraceId(t.Result)),
            await unknown.Content.ReadAsStringAsync().ContinueWith(t => StripTraceId(t.Result)));
        Assert.Equal(HttpStatusCode.Created, (await factory.CreatePortalClient().SignInAsync(email)).StatusCode);
    }

    [Theory]
    [InlineData("short1a")]
    [InlineData("12345678")]
    [InlineData("abcdefgh")]
    public async Task Passwords_outside_the_policy_are_rejected_without_consuming_the_proof(string weakPassword)
    {
        var email = PortalApiFactory.UniqueEmail("student");
        await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();
        var proof = await RequestProofAsync(client, email);

        var rejected = await CompleteResetAsync(client, email, proof, weakPassword);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await PortalClient.ReadJsonAsync(rejected)).GetProperty("errors").TryGetProperty("NewPassword", out _));
        Assert.Equal(HttpStatusCode.Created, (await factory.CreatePortalClient().SignInAsync(email)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteResetAsync(client, email, proof, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_revokes_only_the_session_used_for_it()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var resetBrowser = factory.CreatePortalClient();
        using var otherBrowser = factory.CreatePortalClient();
        await resetBrowser.SignInSuccessfullyAsync(email);
        await otherBrowser.SignInSuccessfullyAsync(email);
        var proof = await RequestProofAsync(resetBrowser, email);
        var resetSessionCredential = resetBrowser.SessionCookie!;

        Assert.Equal(HttpStatusCode.NoContent, (await CompleteResetAsync(resetBrowser, email, proof, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await resetBrowser.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherBrowser.GetCurrentSessionAsync()).StatusCode);

        // Revoked on the server, not merely cleared from the browser.
        using var replay = factory.CreatePortalClient();
        replay.UseSessionCookie(resetSessionCredential);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetCurrentSessionAsync()).StatusCode);
    }

    [Fact]
    public async Task Recovery_requests_require_an_antiforgery_token()
    {
        using var client = factory.CreatePortalClient();

        var response = await client.SendAsync(
            HttpMethod.Post, "/api/auth/password-reset-requests", new { email = "a@iit.test" }, withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    internal static Task<HttpResponseMessage> RequestResetAsync(PortalClient client, string email) =>
        client.SendAsync(HttpMethod.Post, "/api/auth/password-reset-requests", new { email });

    internal static Task<HttpResponseMessage> CompleteResetAsync(PortalClient client, string email, string proof, string newPassword) =>
        client.SendAsync(HttpMethod.Post, "/api/auth/password-resets", new { email, proof, newPassword });

    internal static async Task<string> RequestProofAsync(PortalApiFactory factory, PortalClient client, string email)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await RequestResetAsync(client, email)).StatusCode);
        var message = await factory.Emails.WaitForAsync(email);
        return Uri.UnescapeDataString(ProofPattern().Match(message.Body).Groups[1].Value);
    }

    private Task<string> RequestProofAsync(PortalClient client, string email) => RequestProofAsync(factory, client, email);

    private static string StripTraceId(string json) => TraceIdPattern().Replace(json, string.Empty);

    [GeneratedRegex(@"[?&]proof=([^&\s]+)")]
    private static partial Regex ProofPattern();

    [GeneratedRegex("\"traceId\":\"[^\"]*\"")]
    private static partial Regex TraceIdPattern();
}

public sealed class ExpiredProofFactory : PortalApiFactory
{
    protected override TimeSpan ProofLifespan => TimeSpan.FromSeconds(-1);
}

public sealed class ExpiredProofTests(ExpiredProofFactory factory) : IClassFixture<ExpiredProofFactory>
{
    [Fact]
    public async Task Expired_proof_is_rejected_without_changing_the_password()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();
        var proof = await PasswordRecoveryTests.RequestProofAsync(factory, client, email);

        var response = await PasswordRecoveryTests.CompleteResetAsync(client, email, proof, "Replacement-pass2");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await factory.CreatePortalClient().SignInAsync(email)).StatusCode);
    }
}
