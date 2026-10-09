using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>The initial security events recorded by the existing authentication, session, and recovery flows.</summary>
public sealed class SecurityEventRecordingTests : IDisposable
{
    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static JsonElement Metadata(AuditEvent e) => JsonDocument.Parse(e.MetadataJson ?? "{}").RootElement;

    private async Task<string> EverythingRecordedAsync() =>
        string.Join("\n", (await factory.AuditEventsAsync()).Select(e => JsonSerializer.Serialize(e)))
        + "\n" + string.Join("\n", factory.FallbackSink.Envelopes)
        + "\n" + string.Join("\n", (await factory.OutboxItemsAsync()).Select(i => i.EnvelopeJson));

    [Fact]
    public async Task A_successful_sign_in_records_exactly_one_event_with_the_actor_session_and_server_context()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();

        await client.SignInSuccessfullyAsync(email);

        // One event: the session the sign-in created is part of it, not a separate "session created" event.
        var recorded = Assert.Single(await factory.AuditEventsAsync());
        Assert.Equal("auth.sign-in", recorded.EventType);
        Assert.Equal(AuditEventCategory.Security, recorded.Category);
        Assert.Equal(AuditOutcome.Success, recorded.Outcome);
        Assert.Equal(userId, recorded.ActorUserId);
        Assert.Equal("Session", recorded.EntityType);
        Assert.True(Guid.TryParse(recorded.EntityId, out _));
        Assert.False(string.IsNullOrEmpty(recorded.CorrelationId));
        Assert.Equal(PortalApiFactory.ClientAddress, recorded.Source);
        Assert.Equal(PortalRoles.Student, Metadata(recorded).GetProperty("activeRole").GetString());
    }

    [Fact]
    public async Task A_failed_sign_in_records_no_account_and_unknown_email_looks_the_same_as_a_wrong_password()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        var unknownEmail = PortalApiFactory.UniqueEmail("nobody");
        using var client = factory.CreatePortalClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(email, "Wrong-pass1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(unknownEmail, "Wrong-pass1")).StatusCode);

        var events = await factory.AuditEventsAsync("auth.sign-in");
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(AuditOutcome.Failure, e.Outcome);
            Assert.Null(e.ActorUserId);
            Assert.Null(e.EntityType);
            Assert.Null(e.EntityId);
            Assert.Equal("invalid_credentials", Metadata(e).GetProperty("reason").GetString());
        });
        Assert.Equal(events[0].MetadataJson, events[1].MetadataJson);

        var everything = await EverythingRecordedAsync();
        Assert.DoesNotContain(email, everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unknownEmail, everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Wrong-pass1", everything);
    }

    [Fact]
    public async Task Logout_records_the_revocation_of_that_session_with_its_reason()
    {
        var email = PortalApiFactory.UniqueEmail("coordinator");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Coordinator);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, (await client.SignOutAsync()).StatusCode);

        var events = await factory.AuditEventsAsync();
        var signIn = Assert.Single(events, e => e.EventType == "auth.sign-in");
        var revoked = Assert.Single(events, e => e.EventType == "auth.session.revoked");
        Assert.Equal(AuditOutcome.Success, revoked.Outcome);
        Assert.Equal(userId, revoked.ActorUserId);
        Assert.Equal(signIn.EntityId, revoked.EntityId);
        Assert.Equal("Logout", Metadata(revoked).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task An_idle_timeout_revocation_is_recorded_once_on_the_request_that_revokes_it()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        await factory.WithDbAsync(db => db.AuthSessions
            .Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow.AddHours(-4))));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetCurrentSessionAsync()).StatusCode);

        var revoked = Assert.Single(await factory.AuditEventsAsync("auth.session.revoked"));
        Assert.Equal(userId, revoked.ActorUserId);
        Assert.Equal("IdleTimeout", Metadata(revoked).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Ordinary_authenticated_requests_add_no_events()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        await factory.CreateUserAsync(email, PortalRoles.Admin);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetCurrentSessionAsync()).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/admin")).StatusCode);
        }

        Assert.Single(await factory.AuditEventsAsync());
    }

    [Fact]
    public async Task A_role_switch_records_success_and_denial_without_a_duplicate_access_denied_event()
    {
        var email = PortalApiFactory.UniqueEmail("teacher.coordinator");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Teacher, PortalRoles.Coordinator);
        await factory.SetDefaultRoleAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await client.SetActiveRoleAsync(PortalRoles.Coordinator)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SetActiveRoleAsync(PortalRoles.Admin)).StatusCode);

        var switches = await factory.AuditEventsAsync("auth.role.switched");
        Assert.Equal(2, switches.Count);
        var success = Assert.Single(switches, e => e.Outcome == AuditOutcome.Success);
        Assert.Equal(userId, success.ActorUserId);
        Assert.Equal(PortalRoles.Teacher, Metadata(success).GetProperty("fromRole").GetString());
        Assert.Equal(PortalRoles.Coordinator, Metadata(success).GetProperty("toRole").GetString());
        var denied = Assert.Single(switches, e => e.Outcome == AuditOutcome.Denied);
        Assert.Equal(PortalRoles.Admin, Metadata(denied).GetProperty("toRole").GetString());
        Assert.Empty(await factory.AuditEventsAsync("access.denied"));
    }

    [Fact]
    public async Task A_completed_reset_is_recorded_with_the_revocation_of_the_session_that_used_it()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        var proof = await PasswordRecoveryTests.RequestProofAsync(factory, client, email);

        Assert.Equal(HttpStatusCode.NoContent, (await PasswordRecoveryTests.CompleteResetAsync(client, email, proof, "Replacement-pass2")).StatusCode);

        var completed = Assert.Single(await factory.AuditEventsAsync("auth.password-reset.completed"));
        Assert.Equal(AuditOutcome.Success, completed.Outcome);
        Assert.Equal(userId, completed.ActorUserId);
        var revoked = Assert.Single(await factory.AuditEventsAsync("auth.session.revoked"));
        Assert.Equal("PasswordReset", Metadata(revoked).GetProperty("reason").GetString());

        var everything = await EverythingRecordedAsync();
        Assert.DoesNotContain(proof, everything);
        Assert.DoesNotContain("Replacement-pass2", everything);
    }

    [Fact]
    public async Task A_rejected_reset_records_a_failure_without_an_account_or_the_proof()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        await factory.CreateUserAsync(email, PortalRoles.Admin);
        using var client = factory.CreatePortalClient();

        await PasswordRecoveryTests.CompleteResetAsync(client, email, "not-a-real-proof", "Replacement-pass2");
        await PasswordRecoveryTests.CompleteResetAsync(client, email, "not-a-real-proof", "weak");

        var failures = await factory.AuditEventsAsync("auth.password-reset.completed");
        Assert.Equal(2, failures.Count);
        Assert.All(failures, e =>
        {
            Assert.Equal(AuditOutcome.Failure, e.Outcome);
            Assert.Null(e.ActorUserId);
        });
        Assert.Contains(failures, e => Metadata(e).GetProperty("reason").GetString() == "invalid_proof");
        Assert.Contains(failures, e => Metadata(e).GetProperty("reason").GetString() == "password_rejected");
        Assert.DoesNotContain("not-a-real-proof", await EverythingRecordedAsync());
    }

    [Fact]
    public async Task No_password_cookie_or_session_handle_appears_anywhere_in_what_is_recorded()
    {
        var email = PortalApiFactory.UniqueEmail("coordinator");
        await factory.CreateUserAsync(email, PortalRoles.Coordinator);
        using var client = factory.CreatePortalClient();
        await client.SignInAsync(email, "Wrong-pass1");
        await client.SignInSuccessfullyAsync(email);
        var sessionCookie = client.SessionCookie!;
        await client.SetActiveRoleAsync(PortalRoles.Coordinator);
        await client.SignOutAsync();

        var everything = await EverythingRecordedAsync();

        Assert.DoesNotContain(PortalApiFactory.Password, everything);
        Assert.DoesNotContain("Wrong-pass1", everything);
        Assert.DoesNotContain(sessionCookie, everything);
        Assert.DoesNotContain(email, everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_primary_outcome_is_unchanged_when_the_whole_audit_path_is_down()
    {
        factory.Outbox.FailEnqueue = true;
        factory.FallbackSink.Fail = true;
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(email, "Wrong-pass1")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.SignInAsync(email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SignOutAsync()).StatusCode);

        Assert.True(factory.Metrics.Total("audit.event_loss_risk") > 0);
    }
}
