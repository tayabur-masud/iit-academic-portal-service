using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Students;

/// <summary>
/// US3: eligibility for one admission compares its Degree's required credits and minimum CGPA with the passed-course credits and the
/// current cumulative CGPA published for that same admission, plus its Hall and registration number. Unavailable data is never
/// eligibility, neither value carries between admissions, Student Management never calculates or stores them, and evaluating changes
/// nothing.
/// </summary>
public sealed class GraduationEligibilityTests : StudentTestBase
{
    /// <summary>A results-owner stand-in: published values per admission, and every admission it was asked about.</summary>
    private sealed class FakeResults : IPublishedAdmissionResultsProvider
    {
        public Dictionary<Guid, PublishedAdmissionResults> Published { get; } = [];

        public ConcurrentQueue<Guid> Asked { get; } = new();

        public Task<PublishedAdmissionResults> GetAsync(Guid admissionId, CancellationToken cancellationToken)
        {
            Asked.Enqueue(admissionId);
            return Task.FromResult(Published.GetValueOrDefault(admissionId) ?? PublishedAdmissionResults.Unavailable("No results for this admission."));
        }
    }

    private static FakeResults With(Guid admission, decimal? credits, decimal? cgpa) =>
        new() { Published = { [admission] = PublishedAdmissionResults.Of(credits, cgpa) } };

    private async Task<GraduationEligibility> EvaluateAsync(Guid admissionId, FakeResults results)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var service = new GraduationEligibilityService(scope.ServiceProvider.GetRequiredService<IStudentStore>(), results);
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

    private async Task<Guid> ReadyPgditAsync()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        return await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall: true, registration: true);
    }

    [Fact]
    public async Task Fewer_credits_than_required_is_ineligible_and_names_the_outstanding_credits()
    {
        var id = await ReadyPgditAsync();

        var result = await EvaluateAsync(id, With(id, 30, 3.10m));

        Assert.False(result.Eligible);
        Assert.Equal(36m, result.RequiredCredits);
        Assert.Equal(30m, result.CompletedCredits);
        var credit = Requirement(result, EligibilityRequirement.Credits);
        Assert.Equal(RequirementStatus.NotMet, credit.Status);
        Assert.Contains("6 still needed", credit.Message);
        Assert.Equal(RequirementStatus.Met, Requirement(result, EligibilityRequirement.MinimumCgpa).Status);
    }

    [Theory]
    [InlineData("2.49", false, RequirementStatus.NotMet)]
    [InlineData("2.50", true, RequirementStatus.Met)]
    [InlineData("3.75", true, RequirementStatus.Met)]
    public async Task The_current_cumulative_CGPA_must_reach_the_degree_minimum(string cgpa, bool eligible, RequirementStatus status)
    {
        var id = await ReadyPgditAsync();
        var value = decimal.Parse(cgpa, System.Globalization.CultureInfo.InvariantCulture);

        var result = await EvaluateAsync(id, With(id, 36, value));

        Assert.Equal(eligible, result.Eligible);
        Assert.Equal(2.50m, result.MinimumCgpa);
        Assert.Equal(value, result.CurrentCumulativeCgpa);
        var requirement = Requirement(result, EligibilityRequirement.MinimumCgpa);
        Assert.Equal(status, requirement.Status);
        Assert.Contains(cgpa, requirement.Message);
        Assert.Contains("2.50", requirement.Message);
        Assert.Equal(RequirementStatus.Met, Requirement(result, EligibilityRequirement.Credits).Status);
    }

    [Fact]
    public async Task Meeting_credits_and_CGPA_with_both_identifiers_is_eligible_without_any_clearance_action()
    {
        var id = await ReadyPgditAsync();

        var result = await EvaluateAsync(id, With(id, 36, 2.50m));

        Assert.True(result.Eligible);
        Assert.Equal(
            [EligibilityRequirement.Credits, EligibilityRequirement.MinimumCgpa, EligibilityRequirement.AssignedHall, EligibilityRequirement.RegistrationNumber],
            result.Requirements.Select(r => r.Requirement));
        Assert.All(result.Requirements, r => Assert.Equal(RequirementStatus.Met, r.Status));
    }

    [Theory]
    [InlineData(false, true, EligibilityRequirement.AssignedHall)]
    [InlineData(true, false, EligibilityRequirement.RegistrationNumber)]
    public async Task Enough_credits_and_CGPA_but_a_missing_identifier_is_ineligible_and_names_it(bool hall, bool registration, EligibilityRequirement missing)
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var id = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall, registration);

        var result = await EvaluateAsync(id, With(id, 40, 3.00m));

        Assert.False(result.Eligible);
        Assert.Equal(RequirementStatus.NotMet, Requirement(result, missing).Status);
        Assert.Equal(RequirementStatus.Met, Requirement(result, EligibilityRequirement.Credits).Status);
        Assert.Equal(RequirementStatus.Met, Requirement(result, EligibilityRequirement.MinimumCgpa).Status);
    }

    [Fact]
    public async Task Credits_available_while_the_CGPA_is_not_is_never_eligible_and_says_which_is_missing()
    {
        var id = await ReadyPgditAsync();
        var results = new FakeResults { Published = { [id] = new PublishedAdmissionResults(36, null, null, "The CGPA is being recalculated.") } };

        var result = await EvaluateAsync(id, results);

        Assert.False(result.Eligible);
        Assert.Equal(36m, result.CompletedCredits);
        Assert.Null(result.CurrentCumulativeCgpa);
        Assert.Equal(2.50m, result.MinimumCgpa);
        Assert.Equal(RequirementStatus.Met, Requirement(result, EligibilityRequirement.Credits).Status);
        var cgpa = Requirement(result, EligibilityRequirement.MinimumCgpa);
        Assert.Equal(RequirementStatus.Unavailable, cgpa.Status);
        Assert.Equal("The CGPA is being recalculated.", cgpa.Message);
    }

    [Fact]
    public async Task A_CGPA_available_while_the_credits_are_not_is_never_eligible_and_says_which_is_missing()
    {
        var id = await ReadyPgditAsync();
        var results = new FakeResults { Published = { [id] = new PublishedAdmissionResults(null, "Credits are not published yet.", 3.20m, null) } };

        var result = await EvaluateAsync(id, results);

        Assert.False(result.Eligible);
        Assert.Null(result.CompletedCredits);
        Assert.Equal(3.20m, result.CurrentCumulativeCgpa);
        Assert.Equal(RequirementStatus.Unavailable, Requirement(result, EligibilityRequirement.Credits).Status);
        Assert.Equal("Credits are not published yet.", Requirement(result, EligibilityRequirement.Credits).Message);
        Assert.Equal(RequirementStatus.Met, Requirement(result, EligibilityRequirement.MinimumCgpa).Status);
    }

    [Fact]
    public async Task Unavailable_published_results_are_never_eligible()
    {
        var id = await ReadyPgditAsync();

        var result = await EvaluateAsync(id, new FakeResults());

        Assert.False(result.Eligible);
        Assert.Null(result.CompletedCredits);
        Assert.Null(result.CurrentCumulativeCgpa);
        Assert.Equal(RequirementStatus.Unavailable, Requirement(result, EligibilityRequirement.Credits).Status);
        Assert.Equal(RequirementStatus.Unavailable, Requirement(result, EligibilityRequirement.MinimumCgpa).Status);
    }

    [Fact]
    public async Task A_missing_degree_leaves_both_degree_requirements_unavailable_and_never_eligible()
    {
        var refs = await RefsAsync();
        var id = await ReadyPgditAsync();
        await Host.WithDbAsync(async db =>
        {
            (await db.Degrees.SingleAsync(d => d.Id == refs.Pgdit)).MarkDeleted(DateTime.UtcNow, null);
            await db.SaveChangesAsync();
        });

        var result = await EvaluateAsync(id, With(id, 100, 4.00m));

        Assert.False(result.Eligible);
        Assert.Null(result.RequiredCredits);
        Assert.Null(result.MinimumCgpa);
        Assert.Equal(RequirementStatus.Unavailable, Requirement(result, EligibilityRequirement.Credits).Status);
        var cgpa = Requirement(result, EligibilityRequirement.MinimumCgpa);
        Assert.Equal(RequirementStatus.Unavailable, cgpa.Status);
        Assert.Contains("minimum CGPA", cgpa.Message);
    }

    [Fact]
    public async Task Credits_and_CGPA_are_per_admission_so_a_completed_PGDIT_does_not_make_the_same_Students_EMIT_eligible()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("two-programs");
        var pgdit = await AdmittedAsync(admin, refs.Pgdit, refs.PgditBatch, hall: true, registration: true, email);
        var emit = await AdmittedAsync(admin, refs.Emit, refs.EmitBatch, hall: true, registration: true, email);
        var results = new FakeResults
        {
            Published = { [pgdit] = PublishedAdmissionResults.Of(36, 3.80m), [emit] = PublishedAdmissionResults.Of(12, 2.10m) },
        };

        var pgditResult = await EvaluateAsync(pgdit, results);
        var emitResult = await EvaluateAsync(emit, results);

        Assert.True(pgditResult.Eligible);
        Assert.False(emitResult.Eligible);
        Assert.Equal(48m, emitResult.RequiredCredits);
        Assert.Equal(12m, emitResult.CompletedCredits);
        Assert.Equal(2.10m, emitResult.CurrentCumulativeCgpa);
        Assert.Equal(RequirementStatus.NotMet, Requirement(emitResult, EligibilityRequirement.MinimumCgpa).Status);
        Assert.Equal([pgdit, emit], results.Asked.ToArray());
    }

    [Fact]
    public async Task A_newly_published_result_changes_the_next_evaluation_and_nothing_is_stored()
    {
        var id = await ReadyPgditAsync();
        var results = With(id, 36, 2.40m);
        var before = await AdmissionRowAsync(id);

        var first = await EvaluateAsync(id, results);
        results.Published[id] = PublishedAdmissionResults.Of(36, 2.60m);
        var second = await EvaluateAsync(id, results);

        Assert.False(first.Eligible);
        Assert.True(second.Eligible);
        Assert.Equal(2.60m, second.CurrentCumulativeCgpa);
        Assert.Equal(before.LastModifiedAtUtc, (await AdmissionRowAsync(id)).LastModifiedAtUtc);
    }

    [Fact]
    public async Task The_API_reports_both_unavailable_results_and_the_degree_minimum_and_evaluating_changes_nothing()
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
        Assert.Equal(2.50m, body.GetProperty("minimumCgpa").GetDecimal());
        Assert.False(body.TryGetProperty("completedCredits", out _));
        Assert.False(body.TryGetProperty("currentCumulativeCgpa", out _));
        var requirements = body.GetProperty("requirements").EnumerateArray().ToDictionary(r => r.GetProperty("requirement").GetString()!);
        Assert.Equal("Unavailable", requirements["Credits"].GetProperty("status").GetString());
        Assert.Equal(UnavailablePublishedAdmissionResultsProvider.CreditsReason, requirements["Credits"].GetProperty("message").GetString());
        Assert.Equal("Unavailable", requirements["MinimumCgpa"].GetProperty("status").GetString());
        Assert.Equal(UnavailablePublishedAdmissionResultsProvider.CgpaReason, requirements["MinimumCgpa"].GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal(before.LastModifiedAtUtc, (await AdmissionRowAsync(id)).LastModifiedAtUtc);
        Assert.Equal(events, (await Host.AuditEventsAsync()).Count);
    }

    [Fact]
    public void The_response_carries_published_values_only_when_they_are_available()
    {
        var available = new GraduationEligibility(Guid.NewGuid(), true, 36, 36, 2.50m, 3.10m,
            [new(EligibilityRequirement.MinimumCgpa, RequirementStatus.Met, "Current cumulative CGPA 3.10 meets the minimum 2.50.")]);
        var unavailable = available with { Eligible = false, CompletedCredits = null, CurrentCumulativeCgpa = null };
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var full = JsonSerializer.SerializeToElement(GraduationEligibilityResponse.From(available), web);
        var partial = JsonSerializer.SerializeToElement(GraduationEligibilityResponse.From(unavailable), web);

        Assert.Equal(3.10m, full.GetProperty("currentCumulativeCgpa").GetDecimal());
        Assert.Equal(2.50m, full.GetProperty("minimumCgpa").GetDecimal());
        Assert.False(partial.TryGetProperty("currentCumulativeCgpa", out _));
        Assert.False(partial.TryGetProperty("completedCredits", out _));
        Assert.Equal(2.50m, partial.GetProperty("minimumCgpa").GetDecimal());
    }
}
