using IitAcademicPortal.Application.Abstractions;

namespace IitAcademicPortal.Application.Students;

public enum EligibilityRequirement
{
    Credits,
    AssignedHall,
    RegistrationNumber,
}

public enum RequirementStatus
{
    Met,
    NotMet,
    Unavailable,
}

public sealed record EligibilityRequirementResult(EligibilityRequirement Requirement, RequirementStatus Status, string Message);

/// <summary>
/// The computed graduation eligibility of one admission. <see cref="Eligible"/> is true only when every requirement is met; the credit
/// totals are present only when their sources supplied them.
/// </summary>
public sealed record GraduationEligibility(
    Guid AdmissionId,
    bool Eligible,
    decimal? RequiredCredits,
    decimal? CompletedCredits,
    IReadOnlyList<EligibilityRequirementResult> Requirements);

/// <summary>
/// Works out graduation eligibility for one admission, on demand, from three things: the selected Degree's required credits against
/// the passed-course credits of this admission (from <see cref="ICompletedCreditsProvider"/>), the assigned Hall, and the registration
/// number. Missing or unavailable credit data is never eligibility. Nothing is stored, no result is changed, and result calculation or
/// publication never waits on this check. There is no clearance action. Authorization is the caller's job.
/// </summary>
public sealed class GraduationEligibilityService(IStudentStore store, ICompletedCreditsProvider credits)
{
    public async Task<StudentResult<GraduationEligibility>> EvaluateAsync(Guid admissionId, CancellationToken cancellationToken)
    {
        var admission = await store.FindAdmissionAsync(admissionId, cancellationToken);
        if (admission is null)
        {
            return StudentResult<GraduationEligibility>.NotFound;
        }

        var requirements = new List<EligibilityRequirementResult>();
        var degree = await store.FindDegreeAsync(admission.DegreeId, cancellationToken);
        var required = degree?.CreditsToBeCompleted is > 0 ? degree.CreditsToBeCompleted : null;
        var completed = await credits.GetCompletedCreditsAsync(admission.Id, cancellationToken);
        decimal? completedTotal = completed.Available ? completed.Total : null;

        if (required is null)
        {
            requirements.Add(new(EligibilityRequirement.Credits, RequirementStatus.Unavailable,
                "The selected degree has no required-credit total available."));
        }
        else if (completedTotal is null)
        {
            requirements.Add(new(EligibilityRequirement.Credits, RequirementStatus.Unavailable,
                completed.UnavailableReason ?? "Completed credits from passed course results are not available."));
        }
        else if (completedTotal < required)
        {
            requirements.Add(new(EligibilityRequirement.Credits, RequirementStatus.NotMet,
                $"{completedTotal.Value:0.##} of {required.Value:0.##} required credits completed; {required.Value - completedTotal.Value:0.##} still needed."));
        }
        else
        {
            requirements.Add(new(EligibilityRequirement.Credits, RequirementStatus.Met,
                $"{completedTotal.Value:0.##} of {required.Value:0.##} required credits completed."));
        }

        requirements.Add(admission.AssignedHallId is null
            ? new(EligibilityRequirement.AssignedHall, RequirementStatus.NotMet, "No hall is assigned.")
            : new(EligibilityRequirement.AssignedHall, RequirementStatus.Met, "A hall is assigned."));
        requirements.Add(admission.RegistrationNumber is null
            ? new(EligibilityRequirement.RegistrationNumber, RequirementStatus.NotMet, "No registration number is recorded.")
            : new(EligibilityRequirement.RegistrationNumber, RequirementStatus.Met, "A registration number is recorded."));

        return new StudentResult<GraduationEligibility>(StudentOutcome.Ok, new GraduationEligibility(
            admission.Id, requirements.All(r => r.Status == RequirementStatus.Met), required, completedTotal, requirements));
    }
}
