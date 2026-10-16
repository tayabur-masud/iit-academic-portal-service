using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using IitAcademicPortal.Domain.Notifications;
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

    // --- Durable notification path (feature 005): one logical message per accepted request, and no stored content or proof. ---

    private async Task<List<NotificationRecord>> RecordsForAsync(string userId)
    {
        var records = new List<NotificationRecord>();
        await factory.WithDbAsync(async db => records.AddRange(await db.NotificationRecords.AsNoTracking().Where(r => r.RecipientUserId == userId).ToListAsync()));
        return records;
    }

    [Fact]
    public async Task An_accepted_request_creates_one_logical_notification_with_no_content_or_proof_stored_anywhere()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();

        var proof = await RequestProofAsync(client, email);

        var record = Assert.Single(await RecordsForAsync(userId));
        Assert.Equal(NotificationEventType.PasswordReset, record.EventType);
        Assert.True(record.ContentWithheld);
        Assert.Null(record.Subject);
        Assert.Null(record.Body);
        Assert.Equal(email, record.Destination);

        // Nothing that holds the proof, the link, or the rendered message exists in any notification table, the audit events, or the logs.
        var stored = new List<string>();
        await factory.WithDbAsync(async db =>
        {
            stored.AddRange((await db.NotificationRecords.AsNoTracking().ToListAsync()).Select(r => $"{r.Subject}{r.Body}{r.OccurrenceId}{r.Destination}"));
            stored.AddRange((await db.NotificationAttempts.AsNoTracking().ToListAsync()).Select(a => a.FailureCode ?? string.Empty));
            stored.AddRange((await db.NotificationDispatchStates.AsNoTracking().ToListAsync()).Select(d => d.FailureCode ?? string.Empty));
            stored.AddRange((await db.PasswordRecoveryRequests.AsNoTracking().ToListAsync()).Select(r => r.ProtectedAddress));
            stored.AddRange((await db.AuditEvents.AsNoTracking().ToListAsync()).Select(e => $"{e.MetadataJson}{e.ChangesJson}"));
        });
        Assert.All(stored, text => Assert.DoesNotContain(proof, text));
        var leaks = factory.Logs.Messages.Where(m => m.Contains(proof)).Select(m => m.Length > 600 ? m[..600] : m).ToList();
        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
        // The rendered link (reset page, address, proof) is never logged. The public template wording seeded by the migration may be.
        Assert.DoesNotContain("reset-password?email=", factory.Logs.Everything);
        Assert.Single(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task An_unknown_address_leaves_no_permanent_record_and_no_retained_address()
    {
        var unknown = PortalApiFactory.UniqueEmail("nobody");
        using var client = factory.CreatePortalClient();
        var before = 0;
        await factory.WithDbAsync(async db => before = await db.NotificationRecords.CountAsync());

        Assert.Equal(HttpStatusCode.Accepted, (await RequestResetAsync(client, unknown)).StatusCode);
        await factory.FlushNotificationsAsync();

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(before, await db.NotificationRecords.CountAsync());
            Assert.Empty(await db.PasswordRecoveryRequests.ToListAsync());
        });
        Assert.Empty(factory.Emails.SentTo(unknown));
    }

    [Fact]
    public async Task The_submitted_address_is_retained_only_in_protected_form_until_processed_and_then_removed()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();

        Assert.Equal(HttpStatusCode.Accepted, (await RequestResetAsync(client, email)).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            var retained = await db.PasswordRecoveryRequests.AsNoTracking().ToListAsync();
            Assert.NotEmpty(retained);
            Assert.All(retained, r => Assert.DoesNotContain(email, r.ProtectedAddress, StringComparison.OrdinalIgnoreCase));
        });

        await factory.FlushNotificationsAsync();

        await factory.WithDbAsync(async db => Assert.Empty(await db.PasswordRecoveryRequests.ToListAsync()));
    }

    [Fact]
    public async Task Processing_the_same_retained_request_twice_still_produces_one_notification_and_one_email()
    {
        var email = PortalApiFactory.UniqueEmail("coordinator");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Coordinator);
        var requestId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var recovery = scope.ServiceProvider.GetRequiredService<IitAcademicPortal.Application.PasswordRecovery.PasswordRecoveryService>();
            Assert.True(await recovery.ProcessRequestAsync(requestId, email, CancellationToken.None));
            Assert.True(await recovery.ProcessRequestAsync(requestId, email, CancellationToken.None));
        }

        await factory.FlushNotificationsAsync();

        Assert.Single(await RecordsForAsync(userId));
        Assert.Single(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task A_proof_is_generated_only_for_an_eligible_delivery_so_a_changed_address_sends_nothing()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Accepted, (await RequestResetAsync(client, email)).StatusCode);

        // The address changes after the request was accepted but before delivery: it no longer belongs to the intended recipient.
        // The retained request is resolved into a notification first (as the worker does), and only then does the address change.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var recovery = scope.ServiceProvider.GetRequiredService<IitAcademicPortal.Application.PasswordRecovery.PasswordRecoveryService>();
            Assert.True(await recovery.ProcessRequestAsync(Guid.NewGuid(), email, CancellationToken.None));
        }

        var newEmail = PortalApiFactory.UniqueEmail("student");
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.Email = newEmail;
            user.NormalizedEmail = newEmail.ToUpperInvariant();
            await db.SaveChangesAsync();
        });
        await factory.FlushNotificationsAsync();

        Assert.Empty(factory.Emails.SentTo(email));
        Assert.Empty(factory.Emails.SentTo(newEmail));
        var record = Assert.Single(await RecordsForAsync(userId));
        await factory.WithDbAsync(async db =>
        {
            var state = await db.NotificationDispatchStates.AsNoTracking().SingleAsync(s => s.NotificationId == record.Id);
            Assert.Equal(NotificationStatus.Failed, state.Status);
            Assert.Equal("DestinationChanged", state.FailureCode);
        });
    }

    [Fact]
    public async Task A_retry_after_a_provider_outage_delivers_a_fresh_working_proof()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();
        factory.Emails.Behavior = _ => new IitAcademicPortal.Application.Abstractions.EmailDeliveryException(
            IitAcademicPortal.Application.Abstractions.EmailFailureKind.Retryable, "Http503");
        Assert.Equal(HttpStatusCode.Accepted, (await RequestResetAsync(client, email)).StatusCode);
        await factory.FlushNotificationsAsync();
        Assert.Empty(factory.Emails.SentTo(email));

        factory.Emails.Behavior = null;
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await factory.FlushNotificationsAsync();

        var message = Assert.Single(factory.Emails.SentTo(email));
        var proof = Uri.UnescapeDataString(ProofPattern().Match(message.Body).Groups[1].Value);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteResetAsync(client, email, proof, NewPassword)).StatusCode);
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
