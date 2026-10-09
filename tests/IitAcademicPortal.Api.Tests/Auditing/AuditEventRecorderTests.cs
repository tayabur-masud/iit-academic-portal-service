using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>Event structure, classification, redaction, and size limits at the recorder boundary.</summary>
public sealed class AuditEventRecorderTests : IDisposable
{
    private static readonly AuditEventDefinition TestSecurity = AuditEventDefinition.Security("test.recorder");

    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private AsyncServiceScope NewScope() => factory.Services.CreateAsyncScope();

    private static string UniqueActor() => $"actor-{Guid.NewGuid():N}";

    [Fact]
    public async Task A_security_event_goes_through_the_outbox_and_is_stored_once_with_its_server_facts()
    {
        var actor = UniqueActor();
        await using (var scope = NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().RecordSecurityAsync(
                new AuditEventRequest(TestSecurity, AuditOutcome.Success) { ActorUserId = actor, EntityType = "Session", EntityId = "s-1" });
        }

        var pending = await factory.OutboxItemsAsync();
        Assert.Contains(pending, i => i.State == OutboxDeliveryState.Pending);

        var stored = Assert.Single(await factory.AuditEventsAsync("test.recorder"), e => e.ActorUserId == actor);
        Assert.Equal(AuditEventCategory.Security, stored.Category);
        Assert.Equal(AuditOutcome.Success, stored.Outcome);
        Assert.Equal("Session", stored.EntityType);
        Assert.Equal("s-1", stored.EntityId);
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(DateTimeKind.Utc, stored.OccurredAtUtc.Kind);
        Assert.InRange(stored.OccurredAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.All(await factory.OutboxItemsAsync(), i => Assert.Equal(OutboxDeliveryState.Delivered, i.State));
    }

    [Fact]
    public async Task Every_event_gets_its_own_unique_identifier()
    {
        var actor = UniqueActor();
        await using (var scope = NewScope())
        {
            var recorder = scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>();
            for (var i = 0; i < 5; i++)
            {
                await recorder.RecordSecurityAsync(new AuditEventRequest(TestSecurity, AuditOutcome.Success) { ActorUserId = actor });
            }
        }

        var events = (await factory.AuditEventsAsync("test.recorder")).Where(e => e.ActorUserId == actor).ToList();
        Assert.Equal(5, events.Count);
        Assert.Equal(5, events.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public async Task Anonymous_activity_has_a_null_actor_and_without_a_request_no_correlation_or_source()
    {
        await using (var scope = NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().RecordSecurityAsync(
                new AuditEventRequest(TestSecurity, AuditOutcome.Failure) { EntityType = "Anonymous-case" });
        }

        var stored = Assert.Single(await factory.AuditEventsAsync("test.recorder"), e => e.EntityType == "Anonymous-case");
        Assert.Null(stored.ActorUserId);
        Assert.Null(stored.CorrelationId);
        Assert.Null(stored.Source);
    }

    [Fact]
    public async Task Business_events_cannot_use_the_security_path_and_the_reverse_is_rejected()
    {
        await using var scope = NewScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>();

        Assert.Throws<AuditValidationException>(() =>
            recorder.Stage(new AuditEventRequest(TestSecurity, AuditOutcome.Success)));

        // A mistake on the security path must never change the decision that produced the event.
        var business = AuditEventDefinition.Business("test.business");
        await recorder.RecordSecurityAsync(new AuditEventRequest(business, AuditOutcome.Success));

        Assert.Equal(1, factory.Metrics.Total("audit.event_loss_risk"));
        Assert.Empty((await factory.AuditEventsAsync("test.business")));
    }

    [Theory]
    [InlineData("Bad Type")]
    [InlineData("nodots")]
    [InlineData("Upper.Case")]
    public async Task Event_types_must_be_lower_case_and_dot_separated(string eventType)
    {
        await using var scope = NewScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>();

        Assert.Throws<AuditValidationException>(() =>
            recorder.Stage(new AuditEventRequest(AuditEventDefinition.Business(eventType), AuditOutcome.Success)));
    }

    [Fact]
    public async Task Oversized_values_are_truncated_for_security_events_and_rejected_for_business_events()
    {
        await using var scope = NewScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>();
        var actor = UniqueActor();

        await recorder.RecordSecurityAsync(new AuditEventRequest(TestSecurity, AuditOutcome.Denied)
        {
            ActorUserId = actor,
            EntityId = new string('x', 500),
        });
        var stored = Assert.Single(await factory.AuditEventsAsync("test.recorder"), e => e.ActorUserId == actor);
        Assert.Equal(AuditEvent.MaxEntityIdLength, stored.EntityId!.Length);

        Assert.Throws<AuditValidationException>(() => recorder.Stage(
            new AuditEventRequest(AuditEventDefinition.Business("test.business"), AuditOutcome.Success) { EntityId = new string('x', 500) }));
    }

    [Fact]
    public async Task Oversized_metadata_is_replaced_for_security_events_and_rejected_for_business_events()
    {
        await using var scope = NewScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>();
        var actor = UniqueActor();
        var big = new Dictionary<string, object?> { ["blob"] = string.Join(" ", Enumerable.Repeat("word", AuditEvent.MaxMetadataBytes / 4)) };

        await recorder.RecordSecurityAsync(new AuditEventRequest(TestSecurity, AuditOutcome.Success) { ActorUserId = actor, Metadata = big });
        var stored = Assert.Single(await factory.AuditEventsAsync("test.recorder"), e => e.ActorUserId == actor);
        Assert.Equal("{\"truncated\":true}", stored.MetadataJson);

        Assert.Throws<AuditValidationException>(() => recorder.Stage(
            new AuditEventRequest(AuditEventDefinition.Business("test.business"), AuditOutcome.Success) { Metadata = big }));
    }

    [Fact]
    public async Task Credential_like_metadata_is_redacted_while_identifiers_and_routes_are_kept()
    {
        await using var scope = NewScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>();
        var actor = UniqueActor();
        var sessionHandleLookalike = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE"; // 43 characters, base64url
        var id = Guid.NewGuid();

        await recorder.RecordSecurityAsync(new AuditEventRequest(TestSecurity, AuditOutcome.Success)
        {
            ActorUserId = actor,
            Metadata = new Dictionary<string, object?>
            {
                ["password"] = "hunter2",
                ["note"] = "fine",
                ["value"] = sessionHandleLookalike,
                ["route"] = "api/auth/sessions/current/active-role",
                ["relatedEvent"] = id,
                ["nested"] = new Dictionary<string, object?> { ["token"] = "abc", ["kept"] = "yes" },
            },
        });

        var stored = Assert.Single(await factory.AuditEventsAsync("test.recorder"), e => e.ActorUserId == actor);
        using var json = JsonDocument.Parse(stored.MetadataJson!);
        var root = json.RootElement;
        Assert.Equal(AuditSecretGuard.Redacted, root.GetProperty("password").GetString());
        Assert.Equal(AuditSecretGuard.Redacted, root.GetProperty("value").GetString());
        Assert.Equal("fine", root.GetProperty("note").GetString());
        Assert.Equal("api/auth/sessions/current/active-role", root.GetProperty("route").GetString());
        Assert.Equal(id.ToString(), root.GetProperty("relatedEvent").GetString());
        Assert.Equal(AuditSecretGuard.Redacted, root.GetProperty("nested").GetProperty("token").GetString());
        Assert.Equal("yes", root.GetProperty("nested").GetProperty("kept").GetString());
        Assert.DoesNotContain("hunter2", stored.MetadataJson);
    }

    [Fact]
    public void A_request_carries_no_client_supplied_identity_time_correlation_or_source()
    {
        var names = typeof(AuditEventRequest).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("Id", names);
        Assert.DoesNotContain("EventId", names);
        Assert.DoesNotContain("OccurredAtUtc", names);
        Assert.DoesNotContain("CorrelationId", names);
        Assert.DoesNotContain("Source", names);
    }
}
