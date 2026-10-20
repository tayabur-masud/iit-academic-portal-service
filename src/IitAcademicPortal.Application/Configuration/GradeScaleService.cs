using System.Globalization;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Configuration;

/// <summary>
/// The global Grade scale shared by every Degree. Reads list and show bands; the only write replaces the whole scale, because
/// keeping one active band for every percentage from 0 through 100 is a rule across bands that no single-band change can keep.
/// A replacement is checked in full, then applied under a lock in one transaction: bands with an id are updated (or deactivated),
/// bands without one are created, and live bands left out are soft deleted, each with one audit event. A refused replacement
/// changes nothing. This is reference data only: nothing here assigns a grade or converts marks.
/// </summary>
public sealed class GradeScaleService(
    IGradeScaleStore store,
    IAuditEventRecorder recorder,
    TimeProvider clock,
    IAuditRequestContext request,
    IAuditActorDirectory actors)
{
    private const string EventKey = "grade";
    private const string EntityType = "Grade";

    public async Task<GradeScaleResult> ListAsync(string? search, string? status, CancellationToken cancellationToken)
    {
        var parsed = ConfigurationStatus.Active;
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, ignoreCase: true, out parsed))
        {
            return Invalid(Error("status", "The status must be active, inactive, or all."));
        }

        var normalized = string.IsNullOrWhiteSpace(search) ? null : NameNormalizer.Normalize(search);
        var grades = await store.ListAsync(normalized, parsed, cancellationToken);
        return new GradeScaleResult(GradeScaleOutcome.Ok, Items: await ToBandsAsync(grades, cancellationToken));
    }

    public async Task<GradeScaleResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) is { } grade
            ? new GradeScaleResult(GradeScaleOutcome.Ok, Item: (await ToBandsAsync([grade], cancellationToken))[0])
            : new GradeScaleResult(GradeScaleOutcome.NotFound);

    public async Task<GradeScaleResult> ReplaceAsync(IReadOnlyList<GradeBandInput>? bands, CancellationToken cancellationToken)
    {
        var errors = new ConfigurationErrors();
        if (bands is null || bands.Count == 0)
        {
            errors.Add("bands", "Submit the complete grade scale, with at least one band.");
            return Invalid(errors);
        }

        var proposed = bands.Select((band, index) => Read(band, index, errors)).ToList();
        RejectRepeats(proposed, errors);
        CheckCoverage(proposed, errors);
        if (errors.Any)
        {
            return Invalid(errors);
        }

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        // Concurrent replacements wait here, so each one is checked against, and replaces, the scale the one before it saved.
        await store.LockScaleAsync(cancellationToken);
        var live = (await store.LoadLiveForChangeAsync(cancellationToken)).ToDictionary(g => g.Id);

        foreach (var band in proposed.Where(b => b.Id is { } id && !live.ContainsKey(id)))
        {
            errors.Add($"bands[{band.Index}].id", "This band no longer exists. Reload the scale and try again.");
        }

        if (errors.Any)
        {
            return Invalid(errors);
        }

        // Deletions are saved first, so a letter grade that a deleted band frees can be given to another band in the same scale.
        var kept = proposed.Where(b => b.Id is not null).Select(b => b.Id!.Value).ToHashSet();
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var removed in live.Values.Where(g => !kept.Contains(g.Id)))
        {
            var before = Snapshot(removed);
            removed.MarkDeleted(now, request.ActorUserId);
            Stage(ConfigurationAuditEventDefinitions.Deleted, removed.Id, before, after: null);
        }

        try
        {
            await store.SaveAsync(cancellationToken);
            foreach (var band in proposed)
            {
                if (band.Id is { } id)
                {
                    var grade = live[id];
                    var before = Snapshot(grade);
                    var wasActive = grade.IsActive;
                    band.ApplyTo(grade);
                    var after = Snapshot(grade);
                    if (!before.Keys.All(key => Equals(before[key], after[key])))
                    {
                        Stage(wasActive && !grade.IsActive ? ConfigurationAuditEventDefinitions.Deactivated : ConfigurationAuditEventDefinitions.Updated, grade.Id, before, after);
                    }
                }
                else
                {
                    var grade = new Grade();
                    band.ApplyTo(grade);
                    store.Add(grade);
                    Stage(ConfigurationAuditEventDefinitions.Created, grade.Id, before: null, after: Snapshot(grade));
                }
            }

            await store.SaveAsync(cancellationToken);
        }
        catch (ConfigurationConflictException ex) when (ex.Kind == ConfigurationConflictKind.Duplicate)
        {
            // Only possible when two existing bands swap letter grades in one replacement; nothing is committed.
            errors.Add("bands", "Two bands cannot swap letter grades in one change. Rename one band, save, then rename the other.");
            return Invalid(errors);
        }

        await transaction.CommitAsync(cancellationToken);
        var saved = await store.ListAsync(null, ConfigurationStatus.All, cancellationToken);
        return new GradeScaleResult(GradeScaleOutcome.Ok, Items: await ToBandsAsync(saved, cancellationToken));
    }

    /// <summary>Checks one requested band on its own and reads it into a <see cref="ProposedBand"/>.</summary>
    private static ProposedBand Read(GradeBandInput input, int index, ConfigurationErrors errors)
    {
        string Field(string name) => $"bands[{index}].{name}";

        var letter = NameNormalizer.Display(input.LetterGrade);
        if (letter.Length == 0)
        {
            errors.Add(Field("letterGrade"), "Enter a letter grade.");
        }
        else if (letter.Length > Grade.MaxLetterGradeLength)
        {
            errors.Add(Field("letterGrade"), $"A letter grade can be at most {Grade.MaxLetterGradeLength} characters.");
        }

        bool InRange(decimal? value, string field, string label)
        {
            if (value is null)
            {
                errors.Add(Field(field), $"Enter the {label}.");
                return false;
            }

            if (value < 0 || value > Grade.MaximumMarks)
            {
                errors.Add(Field(field), $"The {label} must be a percentage from 0 to 100.");
                return false;
            }

            return true;
        }

        var lowerValid = InRange(input.MarksLowerBound, "marksLowerBound", "lower bound");
        var upperValid = InRange(input.MarksUpperBound, "marksUpperBound", "upper bound");
        if (lowerValid && upperValid && input.MarksLowerBound >= input.MarksUpperBound)
        {
            errors.Add(Field("marksUpperBound"), "The upper bound must be above the lower bound.");
        }

        var inclusive = input.UpperBoundInclusive ?? false;
        if (inclusive && upperValid && input.MarksUpperBound != Grade.MaximumMarks)
        {
            errors.Add(Field("upperBoundInclusive"), "Only a band that ends at 100% can include its upper bound.");
        }

        if (input.NumericGrade is not { } numeric)
        {
            errors.Add(Field("numericGrade"), "Enter the numeric grade.");
        }
        else if (numeric < 0 || numeric > Grade.MaximumNumericGrade || Scale(numeric) > 2)
        {
            errors.Add(Field("numericGrade"), "Enter a numeric grade from 0.00 to 4.00, with no more than two decimal places.");
        }

        var comment = NameNormalizer.Display(input.Comment);
        if (comment.Length == 0)
        {
            errors.Add(Field("comment"), "Enter a comment.");
        }
        else if (comment.Length > Grade.MaxCommentLength)
        {
            errors.Add(Field("comment"), $"A comment can be at most {Grade.MaxCommentLength} characters.");
        }

        return new ProposedBand(
            index, input.Id, letter, NameNormalizer.Normalize(letter), input.MarksLowerBound ?? 0, input.MarksUpperBound ?? 0, inclusive,
            input.NumericGrade ?? 0, comment, input.IsActive ?? true, lowerValid && upperValid);
    }

    /// <summary>A letter grade or a band id may appear only once in a scale; the later repeat is the one reported.</summary>
    private static void RejectRepeats(List<ProposedBand> bands, ConfigurationErrors errors)
    {
        var letters = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<Guid>();
        foreach (var band in bands)
        {
            if (band.Letter.Length > 0 && !letters.Add(band.NormalizedLetter))
            {
                errors.Add($"bands[{band.Index}].letterGrade", "Another band in this scale already has this letter grade.");
            }

            if (band.Id is { } id && !ids.Add(id))
            {
                errors.Add($"bands[{band.Index}].id", "This band appears more than once in the scale.");
            }
        }
    }

    /// <summary>
    /// The active bands, ordered by lower bound, must start at 0, follow on with no gap or overlap (each lower bound equal to the
    /// upper bound before it), and end at 100 inclusive. Inactive bands are not part of the scale and are not checked here.
    /// </summary>
    private static void CheckCoverage(List<ProposedBand> bands, ConfigurationErrors errors)
    {
        var active = bands.Where(b => b.IsActive).ToList();
        if (active.Count == 0)
        {
            errors.Add("bands", "At least one band must be active.");
            return;
        }

        // Bounds that are missing or out of range are already reported on their band; coverage would only repeat them.
        if (active.Any(b => !b.BoundsReadable))
        {
            return;
        }

        var ordered = active.OrderBy(b => b.Lower).ThenBy(b => b.Upper).ToList();
        if (ordered[0].Lower != 0)
        {
            errors.Add("bands", $"No active band covers 0% to {Percent(ordered[0].Lower)}%. The lowest band must start at 0%.");
            errors.Add($"bands[{ordered[0].Index}].marksLowerBound", "The lowest active band must start at 0%.");
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            var (below, band) = (ordered[i - 1], ordered[i]);
            if (band.Lower > below.Upper)
            {
                errors.Add("bands", $"No active band covers {Percent(below.Upper)}% to {Percent(band.Lower)}%.");
                errors.Add($"bands[{band.Index}].marksLowerBound", $"Leaves a gap: {below.Letter} ends at {Percent(below.Upper)}%.");
            }
            else if (band.Lower < below.Upper)
            {
                errors.Add("bands", $"{below.Letter} and {band.Letter} overlap from {Percent(band.Lower)}% to {Percent(Math.Min(below.Upper, band.Upper))}%.");
                errors.Add($"bands[{band.Index}].marksLowerBound", $"Overlaps {below.Letter}, which ends at {Percent(below.Upper)}%.");
            }
        }

        var top = ordered[^1];
        if (top.Upper != Grade.MaximumMarks)
        {
            errors.Add("bands", $"No active band covers {Percent(top.Upper)}% to 100%. The highest band must end at 100%.");
            errors.Add($"bands[{top.Index}].marksUpperBound", "The highest active band must end at 100%.");
        }
        else if (!top.Inclusive)
        {
            errors.Add("bands", "No active band includes 100%.");
            errors.Add($"bands[{top.Index}].upperBoundInclusive", "The highest active band must include 100%.");
        }
    }

    private static string Percent(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7f;

    private static Dictionary<string, object?> Snapshot(Grade grade) => new(StringComparer.Ordinal)
    {
        ["letterGrade"] = grade.Name,
        ["marksLowerBound"] = grade.MarksLowerBound,
        ["marksUpperBound"] = grade.MarksUpperBound,
        ["upperBoundInclusive"] = grade.UpperBoundInclusive,
        ["numericGrade"] = grade.NumericGrade,
        ["comment"] = grade.Comment,
        ["isActive"] = grade.IsActive,
    };

    private void Stage(string action, Guid id, Dictionary<string, object?>? before, Dictionary<string, object?>? after)
    {
        var keys = (before?.Keys ?? after!.Keys).ToList();
        recorder.Stage(new AuditEventRequest(ConfigurationAuditEventDefinitions.For(EventKey, action), AuditOutcome.Success)
        {
            EntityType = EntityType,
            EntityId = id.ToString(),
            Changes = keys.Select(key => new AuditFieldChange(key, before?.GetValueOrDefault(key), after?.GetValueOrDefault(key))).ToList(),
        });
    }

    /// <summary>The bands with the current email of the accounts that created and last changed them, in one lookup.</summary>
    private async Task<IReadOnlyList<GradeBand>> ToBandsAsync(IReadOnlyList<Grade> grades, CancellationToken cancellationToken)
    {
        var ids = grades.SelectMany(g => new[] { g.CreatedByUserId, g.LastModifiedByUserId }).OfType<string>().Distinct().ToArray();
        var emails = ids.Length == 0 ? new Dictionary<string, string>() : await actors.GetEmailsAsync(ids, cancellationToken);
        string? Email(string? id) => id is not null && emails.TryGetValue(id, out var email) ? email : null;
        return grades.Select(g => new GradeBand(
            g.Id, g.Name, g.MarksLowerBound, g.MarksUpperBound, g.UpperBoundInclusive, g.NumericGrade, g.Comment, g.IsActive,
            g.CreatedAtUtc, g.CreatedByUserId, Email(g.CreatedByUserId), g.LastModifiedAtUtc, g.LastModifiedByUserId, Email(g.LastModifiedByUserId)))
            .ToList();
    }

    private static ConfigurationErrors Error(string field, string message)
    {
        var errors = new ConfigurationErrors();
        errors.Add(field, message);
        return errors;
    }

    private static GradeScaleResult Invalid(ConfigurationErrors errors) => new(GradeScaleOutcome.Invalid, Errors: errors.ToDictionary());

    private sealed record ProposedBand(
        int Index, Guid? Id, string Letter, string NormalizedLetter, decimal Lower, decimal Upper, bool Inclusive,
        decimal Numeric, string Comment, bool IsActive, bool BoundsReadable)
    {
        public void ApplyTo(Grade grade)
        {
            grade.Rename(Letter);
            grade.MarksLowerBound = Lower;
            grade.MarksUpperBound = Upper;
            grade.UpperBoundInclusive = Inclusive;
            grade.NumericGrade = Numeric;
            grade.Comment = Comment;
            grade.IsActive = IsActive;
        }
    }
}
