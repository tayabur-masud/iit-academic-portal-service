namespace IitAcademicPortal.Application.Students;

/// <summary>Completed credits for one admission: a total from passed course results, or a statement that the source cannot give one.</summary>
public sealed record CompletedCredits(bool Available, decimal? Total, string? UnavailableReason = null)
{
    public static CompletedCredits Of(decimal total) => new(true, total);

    public static CompletedCredits Unavailable(string reason) => new(false, null, reason);
}

/// <summary>
/// The read-only boundary to the academic-results owner: the credits of passed courses that count toward one program admission, keyed
/// by <c>StudentAdmissionRecord.Id</c>. Credits from another admission of the same Student are never included (there is no
/// transfer-credit rule). Student Management never stores or edits this total. Until the results owner supplies an adapter, the
/// registered <see cref="UnavailableCompletedCreditsProvider"/> reports the source as unavailable, so nobody is eligible.
/// </summary>
public interface ICompletedCreditsProvider
{
    Task<CompletedCredits> GetCompletedCreditsAsync(Guid admissionId, CancellationToken cancellationToken);
}

/// <summary>Stands in until the academic-results owner provides passed-course credits. It never reports a total.</summary>
public sealed class UnavailableCompletedCreditsProvider : ICompletedCreditsProvider
{
    public const string Reason = "Passed-course results are not available in the portal yet.";

    public Task<CompletedCredits> GetCompletedCreditsAsync(Guid admissionId, CancellationToken cancellationToken) =>
        Task.FromResult(CompletedCredits.Unavailable(Reason));
}
