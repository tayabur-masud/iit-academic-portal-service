using System.Collections.Concurrent;
using System.Net;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Students;

/// <summary>
/// US3: eligibility for one admission is computed from its Degree's required credits against the passed-course credits of that same
/// admission, plus its Hall and registration number. Unavailable credit data is never eligibility, credits never carry between
/// admissions, and evaluating changes nothing.
/// </summary>
public sealed class GraduationEligibilityTests : StudentTestBase
{
    /// <summary>A results owner stand-in: credits per admission, and every admission it was asked about.</summary>
    private sealed class FakeCredits : ICompletedCreditsProvider
    {
        public Dictionary<Guid, CompletedCredits> Totals { get; } = [];

        public ConcurrentQueue<Guid> Asked { get; } = new();

        public Task<CompletedCredits> GetCompletedCreditsAsync(Guid admissionId, CancellationToken cancellationToken)
        {
            Asked.Enqueue(admissionId);
            return Task.FromResult(Totals.GetValueOrDefault(admissionId) ?? CompletedCredits.Unavailable("No results for this admission."));
        }
    }

    private async Task<GraduationEligibility> EvaluateAsync(Guid admissionId, FakeCredits credits)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var service = new GraduationEligibilityService(scope.ServiceProvider.GetRequiredService<IStudentStore>(), credits);
        var result = await service.EvaluateAsync(admissionId, CancellationToken.None);
        Assert.True(result.Succeeded);
        return result.Value!;
    }

    private async Task<Guid> AdmittedAsync(Infrastructure.PortalClient admin, Guid degree, Guid batch, bool hall, bool registration, string? email = null)
    {
        var refs = await RefsAsync();
        var form = Form(email ?? Unique("grad"), degree, batch);
        form["assignedHallId"] = hall ? refs.Hall : null;
        form["registrationNumber"] = registration ? "REG-" + Guid.NewGuid().ToString("N")[..8] : null;
        return (await AdmitAsync(admin, form)).Id;
    }

    private static EligibilityRequirementResult Requirement(GraduationEligibility result, EligibilityRequirement requirement) =>
        result.Requirements.Single(r => r.Requirement == requirement);

    [Fact]
    public async Task Fewer_credits_than_required_is_ineligible_and_names_the_outstanding_credits()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var id = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall: true, registration: true);
        var credits = new FakeCredits { Totals = { [id] = CompletedCredits.Of(30) } };

        var result = await EvaluateAsync(id, credits);

        Assert.False(result.Eligible);
        Assert.Equal(36m, result.RequiredCredits);
        Assert.Equal(30m, result.CompletedCredits);
        var credit = Requirement(result, EligibilityRequirement.Credits);
        Assert.Equal(RequirementStatus.NotMet, credit.Status);
        Assert.Contains("6 still needed", credit.Message);
    }

    [Fact]
    public async Task Exactly_the_required_credits_with_both_identifiers_is_eligible_without_any_clearance_action()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var id = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall: true, registration: true);

        var result = await EvaluateAsync(id, new FakeCredits { Totals = { [id] = CompletedCredits.Of(36) } });

        Assert.True(result.Eligible);
        Assert.All(result.Requirements, r => Assert.Equal(RequirementStatus.Met, r.Status));
    }

    [Theory]
    [InlineData(false, true, EligibilityRequirement.AssignedHall)]
    [InlineData(true, false, EligibilityRequirement.RegistrationNumber)]
    public async Task Enough_credits_but_a_missing_identifier_is_ineligible_and_names_it(bool hall, bool registration, EligibilityRequirement missing)
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var id = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall, registration);

        var result = await EvaluateAsync(id, new FakeCredits { Totals = { [id] = CompletedCredits.Of(40) } });

        Assert.False(result.Eligible);
        Assert.Equal(RequirementStatus.NotMet, Requirement(result, missing).Status);
        Assert.Equal(RequirementStatus.Met, Requirement(result, EligibilityRequirement.Credits).Status);
    }

    [Fact]
    public async Task Unavailable_completed_credits_are_never_eligibility()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var id = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall: true, registration: true);

        var result = await EvaluateAsync(id, new FakeCredits());

        Assert.False(result.Eligible);
        Assert.Null(result.CompletedCredits);
        Assert.Equal(RequirementStatus.Unavailable, Requirement(result, EligibilityRequirement.Credits).Status);
    }

    [Fact]
    public async Task A_missing_degree_requirement_is_unavailable_and_never_eligible()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var id = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall: true, registration: true);
        await Host.WithDbAsync(async db =>
        {
            (await db.Degrees.SingleAsync(d => d.Id == refs.Pgdit)).MarkDeleted(DateTime.UtcNow, null);
            await db.SaveChangesAsync();
        });

        var result = await EvaluateAsync(id, new FakeCredits { Totals = { [id] = CompletedCredits.Of(100) } });

        Assert.False(result.Eligible);
        Assert.Null(result.RequiredCredits);
        Assert.Equal(RequirementStatus.Unavailable, Requirement(result, EligibilityRequirement.Credits).Status);
    }

    [Fact]
    public async Task Credits_are_per_admission_so_a_completed_PGDIT_does_not_make_the_same_Students_EMIT_eligible()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("two-programs");
        var pgdit = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall: true, registration: true, email);
        var emit = await AdmittedAsync(admin, refs.Emit, refs.EmitBatch, hall: true, registration: true, email);
        var credits = new FakeCredits { Totals = { [pgdit] = CompletedCredits.Of(36), [emit] = CompletedCredits.Of(12) } };

        var pgditResult = await EvaluateAsync(pgdit, credits);
        var emitResult = await EvaluateAsync(emit, credits);

        Assert.True(pgditResult.Eligible);
        Assert.False(emitResult.Eligible);
        Assert.Equal(48m, emitResult.RequiredCredits);
        Assert.Equal(12m, emitResult.CompletedCredits);
        Assert.Equal([pgdit, emit], credits.Asked.ToArray());
    }

    [Fact]
    public async Task The_API_reports_the_unavailable_results_source_and_evaluating_changes_nothing()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var id = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall: true, registration: true);
        var before = await AdmissionRowAsync(id);
        var events = (await Host.AuditEventsAsync()).Count;

        var (status, body) = await SendAsync(admin, HttpMethod.Get, $"{Students}/{id}/graduation-eligibility");
        var (missing, _) = await SendAsync(admin, HttpMethod.Get, $"{Students}/{Guid.NewGuid()}/graduation-eligibility");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("eligible").GetBoolean());
        Assert.Equal(36m, body.GetProperty("requiredCredits").GetDecimal());
        Assert.False(body.TryGetProperty("completedCredits", out _));
        var credit = body.GetProperty("requirements").EnumerateArray().Single(r => r.GetProperty("requirement").GetString() == "Credits");
        Assert.Equal("Unavailable", credit.GetProperty("status").GetString());
        Assert.Equal(UnavailableCompletedCreditsProvider.Reason, credit.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal(before.LastModifiedAtUtc, (await AdmissionRowAsync(id)).LastModifiedAtUtc);
        Assert.Equal(events, (await Host.AuditEventsAsync()).Count);
    }
}
