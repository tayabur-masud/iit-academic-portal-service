using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>
/// No credential or non-allowlisted value reaches stored events, outbox envelopes, the fallback sink, or the
/// ordinary application logs (SC-003). Event content never reaches the logs either.
/// </summary>
public sealed class AuditRedactionTests : IDisposable
{
    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task<string> StoredAsync()
    {
        var events = await factory.AuditEventsAsync();
        var outbox = await factory.OutboxItemsAsync();
        return string.Join("\n", events.Select(e => $"{e.EntityId} {e.MetadataJson} {e.ChangesJson}"))
            + "\n" + string.Join("\n", outbox.Select(i => i.EnvelopeJson))
            + "\n" + string.Join("\n", factory.FallbackSink.Envelopes);
    }

    [Fact]
    public async Task A_complete_sign_in_reset_and_sign_out_flow_leaves_no_secret_in_storage_or_logs()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();

        await client.SignInAsync(email, "Wrong-pass1");
        await client.SignInSuccessfullyAsync(email);
        var sessionCookie = client.SessionCookie!;
        var proof = await PasswordRecoveryTests.RequestProofAsync(factory, client, email);
        await PasswordRecoveryTests.CompleteResetAsync(client, email, proof, "Replacement-pass2");
        await PasswordRecoveryTests.CompleteResetAsync(client, email, "bogus-proof-value", "Another-pass3");

        var stored = await StoredAsync();
        var logs = factory.Logs.Everything;
        foreach (var secret in new[] { PortalApiFactory.Password, "Wrong-pass1", "Replacement-pass2", "Another-pass3", sessionCookie, proof, "bogus-proof-value" })
        {
            Assert.DoesNotContain(secret, stored);
            Assert.DoesNotContain(secret, logs);
        }
    }

    [Fact]
    public async Task Staged_business_events_keep_only_approved_values_in_storage()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().Stage(
                new AuditEventRequest(AuditEventDefinition.Business("record.updated", "status"), AuditOutcome.Success)
                {
                    EntityType = "Record",
                    EntityId = "r-1",
                    Metadata = new Dictionary<string, object?> { ["note"] = "visible", ["password"] = "metadata-secret" },
                    Changes = [new AuditFieldChange("status", "draft", "final"), new AuditFieldChange("salary", "1000", "9999")],
                });
            await scope.ServiceProvider.GetRequiredService<IitAcademicPortal.Infrastructure.Persistence.PortalDbContext>().SaveChangesAsync();
        }

        var stored = await StoredAsync();
        Assert.Contains("final", stored);
        Assert.DoesNotContain("9999", stored);
        Assert.DoesNotContain("salary", stored);
        Assert.DoesNotContain("metadata-secret", stored);
        Assert.Contains("visible", stored);
    }

    [Fact]
    public async Task Event_content_is_never_written_to_the_ordinary_logs()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().RecordSecurityAsync(
                new AuditEventRequest(AuditEventDefinition.Security("test.logs"), AuditOutcome.Success)
                {
                    ActorUserId = "actor-for-log-test",
                    Metadata = new Dictionary<string, object?> { ["marker"] = "distinct-marker-8675309" },
                });
        }

        await factory.FlushAuditAsync();
        factory.Outbox.FailEnqueue = true;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().RecordSecurityAsync(
                new AuditEventRequest(AuditEventDefinition.Security("test.logs"), AuditOutcome.Failure)
                {
                    Metadata = new Dictionary<string, object?> { ["marker"] = "distinct-marker-failure-path" },
                });
        }

        // Even when the write fails and a critical log is written, it carries the exception type, not the payload.
        Assert.Contains(factory.Logs.Messages, m => m.Contains("could not be written to the outbox"));
        Assert.DoesNotContain("distinct-marker-8675309", factory.Logs.Everything);
        Assert.DoesNotContain("distinct-marker-failure-path", factory.Logs.Everything);
        Assert.DoesNotContain("actor-for-log-test", factory.Logs.Everything);
    }

    [Fact]
    public async Task Allowed_and_denied_requests_record_no_query_string_or_cookie_values()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/admin?token=query-secret-value")).StatusCode);

        Assert.DoesNotContain("query-secret-value", await StoredAsync());
        Assert.DoesNotContain("query-secret-value", factory.Logs.Everything);
    }
}
