using IitAcademicPortal.Api.Tests.Auditing.Fixtures;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// The rule that keeps future entities honest: every business entity derives from <see cref="AuditedEntity"/> and so records who
/// created and last changed it, and when. A new entity that is neither derived nor listed below fails this test. Add an entry here
/// only for infrastructure that records the same facts another way, and say how.
/// </summary>
public sealed class AuditedEntityConventionTests : IDisposable
{
    /// <summary>Entities that deliberately do not derive from <see cref="AuditedEntity"/>, with the reason.</summary>
    private static readonly IReadOnlyDictionary<Type, string> Exempt = new Dictionary<Type, string>
    {
        [typeof(PortalUser)] = "An ASP.NET Core Identity account; its lifecycle is managed by Identity, and sign-in activity is kept in AuthSession.",
        [typeof(AuthSession)] = "Infrastructure: a sign-in session; it records CreatedAt, LastActivityAt, and its revocation itself.",
        [typeof(AuditEvent)] = "Append-only audit history; OccurredAtUtc and ActorUserId are its own creator and time, and it is never changed.",
        [typeof(SecurityAuditOutboxItem)] = "Infrastructure: the delivery queue for security events, with its own enqueue, attempt, and delivery times.",
        [typeof(NotificationRecord)] = "Append-only notification history; RequestedAtUtc is its own time, and it is never changed (the runtime role cannot update it).",
        [typeof(NotificationDispatchState)] = "Infrastructure: mutable delivery state for one notification, with its own UpdatedAtUtc; the notification record holds who and when.",
        [typeof(NotificationAttempt)] = "Append-only delivery attempt fact with its own start and completion times; never changed.",
        [typeof(PasswordRecoveryRequest)] = "Infrastructure: a transient, protected recovery request with its own request and expiry times, deleted after processing.",
        [typeof(FeedbackReminderSchedule)] = "Infrastructure: scheduling state with its own UpdatedAtUtc; it holds identifiers and times only.",
        [typeof(StandInBusinessEntity)] = "A test-only stand-in entity that exists only in the test model.",
    };

    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task<List<Microsoft.EntityFrameworkCore.Metadata.IEntityType>> EntityTypesAsync()
    {
        var types = new List<Microsoft.EntityFrameworkCore.Metadata.IEntityType>();
        await factory.WithDbAsync(db =>
        {
            types.AddRange(db.Model.GetEntityTypes().Where(t => !t.IsOwned()));
            return Task.CompletedTask;
        });
        return types;
    }

    private static bool IsIdentityFramework(Type type) => type.Namespace?.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) == true;

    [Fact]
    public async Task Every_entity_derives_from_AuditedEntity_unless_it_is_listed_as_exempt_with_a_reason()
    {
        var offenders = (await EntityTypesAsync())
            .Select(t => t.ClrType)
            .Where(t => !typeof(AuditedEntity).IsAssignableFrom(t) && !IsIdentityFramework(t) && !Exempt.ContainsKey(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These entities do not record who created and last changed them and when: {string.Join(", ", offenders)}. " +
            "Derive them from IitAcademicPortal.Domain.AuditedEntity (the columns and the stamping are automatic), or, for " +
            "infrastructure that records the same facts another way, add them to Exempt with the reason.");
    }

    [Fact]
    public async Task Every_exemption_is_still_a_real_entity_that_does_not_derive_from_the_base()
    {
        var clrTypes = (await EntityTypesAsync()).Select(t => t.ClrType).ToHashSet();

        foreach (var (type, reason) in Exempt)
        {
            Assert.Contains(type, clrTypes);
            Assert.False(typeof(AuditedEntity).IsAssignableFrom(type), $"{type.Name} now derives from AuditedEntity; remove its exemption.");
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
    }

    [Fact]
    public async Task Every_audited_entity_maps_the_four_columns_with_required_times_and_bounded_account_ids()
    {
        var audited = (await EntityTypesAsync()).Where(t => typeof(AuditedEntity).IsAssignableFrom(t.ClrType)).ToList();

        Assert.True(audited.Count >= 13, "The configuration entities are audited entities.");
        foreach (var type in audited)
        {
            foreach (var time in new[] { nameof(AuditedEntity.CreatedAtUtc), nameof(AuditedEntity.LastModifiedAtUtc) })
            {
                var property = type.FindProperty(time);
                Assert.True(property is not null, $"{type.ClrType.Name}.{time} is not mapped.");
                Assert.False(property!.IsNullable, $"{type.ClrType.Name}.{time} must be required.");
                Assert.False(string.IsNullOrWhiteSpace(property.GetDefaultValueSql()), $"{type.ClrType.Name}.{time} needs a database default for rows added by SQL.");
            }

            foreach (var by in new[] { nameof(AuditedEntity.CreatedByUserId), nameof(AuditedEntity.LastModifiedByUserId) })
            {
                var property = type.FindProperty(by);
                Assert.True(property is not null, $"{type.ClrType.Name}.{by} is not mapped.");
                Assert.True(property!.IsNullable, $"{type.ClrType.Name}.{by} must allow no account.");
                Assert.Equal(AuditedEntity.MaxUserIdLength, property.GetMaxLength());
            }
        }
    }
}
