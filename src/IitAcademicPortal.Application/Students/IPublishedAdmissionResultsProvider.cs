namespace IitAcademicPortal.Application.Students;

/// <summary>
/// What published course results say about one admission: its completed passed-course credits and its current cumulative CGPA, each
/// either a value or the reason the source cannot give one. The two are independent; either may be available without the other.
/// </summary>
public sealed record PublishedAdmissionResults(
    decimal? CompletedCredits,
    string? CreditsUnavailableReason,
    decimal? CurrentCumulativeCgpa,
    string? CgpaUnavailableReason)
{
    public static PublishedAdmissionResults Of(decimal? completedCredits, decimal? currentCumulativeCgpa) =>
        new(completedCredits, null, currentCumulativeCgpa, null);

    public static PublishedAdmissionResults Unavailable(string reason) => new(null, reason, null, reason);
}

/// <summary>
/// The read-only boundary to the academic-results owner, keyed by <c>StudentAdmissionRecord.Id</c>. The owner calculates the current
/// cumulative CGPA from published course results using the configured Grade numeric values, and recalculates it as results publish;
/// Student Management only reads both values, never calculates or stores them, and never uses another admission's. Until the owner
/// registers its adapter (tasks T022 and T026), <see cref="UnavailablePublishedAdmissionResultsProvider"/> reports both as unavailable,
/// so nobody is eligible.
/// </summary>
public interface IPublishedAdmissionResultsProvider
{
    Task<PublishedAdmissionResults> GetAsync(Guid admissionId, CancellationToken cancellationToken);
}

/// <summary>Stands in until the academic-results owner publishes its contract. It never reports a value.</summary>
public sealed class UnavailablePublishedAdmissionResultsProvider : IPublishedAdmissionResultsProvider
{
    public const string CreditsReason = "Passed-course results are not available in the portal yet.";

    public const string CgpaReason = "The current cumulative CGPA from published results is not available in the portal yet.";

    public Task<PublishedAdmissionResults> GetAsync(Guid admissionId, CancellationToken cancellationToken) =>
        Task.FromResult(new PublishedAdmissionResults(null, CreditsReason, null, CgpaReason));
}
