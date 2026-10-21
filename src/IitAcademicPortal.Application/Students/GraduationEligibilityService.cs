using IitAcademicPortal.Application.Abstractions;

namespace IitAcademicPortal.Application.Students;

public enum EligibilityRequirement
{
    Credits,
    MinimumCgpa,
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
/// The computed graduation eligibility of one admission. <see cref="Eligible"/> is true only when every requirement is met; each number is
/// present only when its source supplied it.
/// </summary>
public sealed record GraduationEligibility(
    Guid AdmissionId,
    bool Eligible,
    decimal? RequiredCredits,
    decimal? CompletedCredits,
    decimal? MinimumCgpa,
    decimal? CurrentCumulativeCgpa,
    IReadOnlyList<EligibilityRequirementResult> Requirements);

/// <summary>
/// Works out graduation eligibility for one admission, on demand, from four things: the selected Degree's required credits against the
/// passed-course credits of this admission, the Degree's minimum CGPA against this admission's current cumulative CGPA (both published
/// values from <see cref="IPublishedAdmissionResultsProvider"/>), the assigned Hall, and the registration number. Missing or unavailable
/// data is never eligibility. Nothing is stored, no result is changed, and result calculation or
/// publication never waits on this check. There is no clearance action. Authorization is the caller's job.
/// </summary>
public sealed class GraduationEligibilityService(IStudentStore store, IPublishedAdmissionResultsProvider results)
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
        var minimum = degree?.MinimumCgpaForCompletion is > 0 ? degree.MinimumCgpaForCompletion : null;
        var published = await results.GetAsync(admission.Id, cancellationToken);
        var completedTotal = published.CompletedCredits;
        var cgpa = published.CurrentCumulativeCgpa;

        if (required is null)
        {
            requirements.Add(new(EligibilityRequirement.Credits, RequirementStatus.Unavailable,
                "The selected degree has no required-credit total available."));
        }
        else if (completedTotal is null)
        {
            requirements.Add(new(EligibilityRequirement.Credits, RequirementStatus.Unavailable,
                published.CreditsUnavailableReason ?? "Completed credits from passed course results are not available."));
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

        if (minimum is null)
        {
            requirements.Add(new(EligibilityRequirement.MinimumCgpa, RequirementStatus.Unavailable,
                "The selected degree has no minimum CGPA for completion available."));
        }
        else if (cgpa is null)
        {
            requirements.Add(new(EligibilityRequirement.MinimumCgpa, RequirementStatus.Unavailable,
                published.CgpaUnavailableReason ?? "The current cumulative CGPA from published results is not available."));
        }
        else if (cgpa < minimum)
        {
            requirements.Add(new(EligibilityRequirement.MinimumCgpa, RequirementStatus.NotMet,
                $"Current cumulative CGPA {cgpa.Value:0.00} is below the minimum {minimum.Value:0.00}."));
        }
        else
        {
            requirements.Add(new(EligibilityRequirement.MinimumCgpa, RequirementStatus.Met,
                $"Current cumulative CGPA {cgpa.Value:0.00} meets the minimum {minimum.Value:0.00}."));
        }

        requirements.Add(admission.AssignedHallId is null
            ? new(EligibilityRequirement.AssignedHall, RequirementStatus.NotMet, "No hall is assigned.")
            : new(EligibilityRequirement.AssignedHall, RequirementStatus.Met, "A hall is assigned."));
        requirements.Add(admission.RegistrationNumber is null
            ? new(EligibilityRequirement.RegistrationNumber, RequirementStatus.NotMet, "No registration number is recorded.")
            : new(EligibilityRequirement.RegistrationNumber, RequirementStatus.Met, "A registration number is recorded."));

        return new StudentResult<GraduationEligibility>(StudentOutcome.Ok, new GraduationEligibility(
            admission.Id, requirements.All(r => r.Status == RequirementStatus.Met), required, completedTotal, minimum, cgpa, requirements));
    }
}
