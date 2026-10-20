using System.Linq.Expressions;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Configuration;

/// <summary>
/// Whether live records still refer to a record, which blocks deleting it. A resource nothing refers to passes no function.
/// </summary>
public delegate Task<bool> DependentsCheck<in T>(T entity, ConfigurationContext context, CancellationToken cancellationToken);

/// <summary>A catalog with only a name and an active flag.</summary>
public sealed class NameOnlyResource<T>(string route, string displayName, string eventKey, Func<T> create, DependentsCheck<T>? dependents = null)
    : ConfigurationResource<T>(route, displayName, eventKey, ConfigurationFields.None) where T : ConfigurationRecord
{
    protected override T NewRecord() => create();

    protected override Task<bool> HasDependentsAsync(T entity, ConfigurationContext context, CancellationToken cancellationToken) =>
        dependents is null ? Task.FromResult(false) : dependents(entity, context, cancellationToken);
}

/// <summary>A catalog with an optional code that is unique within the catalog.</summary>
public class CodedResource<T>(
    string route, string displayName, string eventKey, Func<T> create, ConfigurationFields extra = ConfigurationFields.None, DependentsCheck<T>? dependents = null)
    : ConfigurationResource<T>(route, displayName, eventKey, ConfigurationFields.Code | extra) where T : ConfigurationRecord, ICodedRecord
{
    protected override T NewRecord() => create();

    protected override Task<bool> HasDependentsAsync(T entity, ConfigurationContext context, CancellationToken cancellationToken) =>
        dependents is null ? Task.FromResult(false) : dependents(entity, context, cancellationToken);

    protected override Task<bool> CodeTakenAsync(ConfigurationContext context, T entity, string normalizedCode, CancellationToken cancellationToken) =>
        context.Store.CodeExistsAsync<T>(normalizedCode, entity.Id, cancellationToken);
}

/// <summary>A school, college, or university: a fixed type plus an optional code and address.</summary>
public sealed class InstitutionResource()
    : CodedResource<EducationalInstitution>(
        "institutions", "Educational institution", "institution", () => new EducationalInstitution(),
        ConfigurationFields.Type | ConfigurationFields.Address)
{
    protected override Expression<Func<EducationalInstitution, bool>>? TypeFilter(string type, ConfigurationErrors errors)
    {
        if (TryParseType<EducationalInstitutionType>(type, out var value))
        {
            return institution => institution.Type == value;
        }

        errors.Add("type", "The type must be School, College, or University.");
        return null;
    }

    protected override Task ApplyAsync(
        EducationalInstitution entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Type))
        {
            errors.Add("type", "Select a type: School, College, or University.");
        }
        else if (Enum.TryParse<EducationalInstitutionType>(input.Type.Trim(), ignoreCase: true, out var type) && Enum.IsDefined(type))
        {
            entity.Type = type;
        }
        else
        {
            errors.Add("type", "The type must be School, College, or University.");
        }

        entity.Address = ReadAddress(input.Address, errors);
        return Task.CompletedTask;
    }

    protected override Task<bool> HasDependentsAsync(EducationalInstitution entity, ConfigurationContext context, CancellationToken cancellationToken) =>
        context.Store.UsedByStudentsAsync<EducationalInstitution>(entity.Id, cancellationToken);

    protected override void Describe(EducationalInstitution entity, Dictionary<string, object?> values)
    {
        values["type"] = entity.Type.ToString();
        values["address"] = entity.Address;
    }

    protected override ConfigurationItem Project(EducationalInstitution entity, ConfigurationItem item) =>
        item with { Type = entity.Type.ToString(), Address = entity.Address };
}

/// <summary>A university residential hall: an optional unique code and an optional address. Independent of every other resource.</summary>
public sealed class HallResource()
    : CodedResource<Hall>("halls", "Hall", "hall", () => new Hall(), ConfigurationFields.Address)
{
    protected override Task ApplyAsync(
        Hall entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken)
    {
        entity.Address = ReadAddress(input.Address, errors);
        return Task.CompletedTask;
    }

    protected override Task<bool> HasDependentsAsync(Hall entity, ConfigurationContext context, CancellationToken cancellationToken) =>
        context.Store.UsedByStudentsAsync<Hall>(entity.Id, cancellationToken);

    protected override void Describe(Hall entity, Dictionary<string, object?> values) => values["address"] = entity.Address;

    protected override ConfigurationItem Project(Hall entity, ConfigurationItem item) => item with { Address = entity.Address };
}

/// <summary>
/// A degree with its completion requirements: the credits to be completed, which Student Management uses for graduation
/// eligibility, and the minimum CGPA (on the 4.00 scale), which Configuration stores for later use.
/// </summary>
public sealed class DegreeResource(DependentsCheck<Degree>? dependents = null)
    : CodedResource<Degree>("degrees", "Degree", "degree", () => new Degree(), ConfigurationFields.Credits, dependents)
{
    private const decimal MaximumCreditsToBeCompleted = 999_999.99m;

    public const decimal MaximumCgpa = 4.00m;

    protected override Task ApplyAsync(
        Degree entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken)
    {
        if (input.CreditsToBeCompleted is not { } credits)
        {
            errors.Add("creditsToBeCompleted", "Enter the credits to be completed.");
        }
        else
        {
            var scale = (decimal.GetBits(credits)[3] >> 16) & 0x7f;
            if (credits <= 0 || credits > MaximumCreditsToBeCompleted || scale > 2)
            {
                errors.Add("creditsToBeCompleted", "Enter a positive credit total with no more than two decimal places.");
            }
            else
            {
                entity.SetCreditsToBeCompleted(credits);
            }
        }

        if (input.MinimumCgpaForCompletion is not { } cgpa)
        {
            errors.Add("minimumCgpaForCompletion", "Enter the minimum CGPA for completion.");
        }
        else if (cgpa <= 0 || cgpa > MaximumCgpa || ((decimal.GetBits(cgpa)[3] >> 16) & 0x7f) > 2)
        {
            errors.Add("minimumCgpaForCompletion", "Enter a CGPA greater than 0 and at most 4.00, with no more than two decimal places.");
        }
        else
        {
            entity.SetMinimumCgpaForCompletion(cgpa);
        }

        return Task.CompletedTask;
    }

    protected override void Describe(Degree entity, Dictionary<string, object?> values)
    {
        values["creditsToBeCompleted"] = entity.CreditsToBeCompleted;
        values["minimumCgpaForCompletion"] = entity.MinimumCgpaForCompletion;
    }

    protected override ConfigurationItem Project(Degree entity, ConfigurationItem item) =>
        item with { CreditsToBeCompleted = entity.CreditsToBeCompleted, MinimumCgpaForCompletion = entity.MinimumCgpaForCompletion };
}

/// <summary>
/// A Batch of one Degree, starting in one Semester, in one Academic Session. All three references are required and
/// must be active when chosen. The name is unique within the Degree, whatever the Session.
/// </summary>
public sealed class BatchResource()
    : ConfigurationResource<Batch>("batches", "Batch", "batch", ConfigurationFields.Degree | ConfigurationFields.Semester | ConfigurationFields.Session)
{
    protected override Batch NewRecord() => new();

    protected override string DuplicateNameMessage => "A batch with this name already exists for this degree.";

    protected override Expression<Func<Batch, bool>> InScope(Batch candidate)
    {
        var degreeId = candidate.DegreeId;
        return batch => batch.DegreeId == degreeId;
    }

    protected override async Task ApplyAsync(
        Batch entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken)
    {
        entity.DegreeId = await ValidateReferenceAsync<Degree>(
            input.DegreeId, entity.DegreeId, isNew, "degreeId", "degree", context, errors, cancellationToken);
        entity.StartingSemesterId = await ValidateReferenceAsync<Semester>(
            input.StartingSemesterId, entity.StartingSemesterId, isNew, "startingSemesterId", "starting semester", context, errors, cancellationToken);
        entity.AcademicSessionId = await ValidateReferenceAsync<AcademicSession>(
            input.AcademicSessionId, entity.AcademicSessionId, isNew, "academicSessionId", "academic session", context, errors, cancellationToken);
    }

    protected override Task<bool> HasDependentsAsync(Batch entity, ConfigurationContext context, CancellationToken cancellationToken) =>
        context.Store.UsedByStudentsAsync<Batch>(entity.Id, cancellationToken);

    protected override void Describe(Batch entity, Dictionary<string, object?> values)
    {
        values["degreeId"] = entity.DegreeId;
        values["startingSemesterId"] = entity.StartingSemesterId;
        values["academicSessionId"] = entity.AcademicSessionId;
    }

    protected override ConfigurationItem Project(Batch entity, ConfigurationItem item) =>
        item with { DegreeId = entity.DegreeId, StartingSemesterId = entity.StartingSemesterId, AcademicSessionId = entity.AcademicSessionId };
}

/// <summary>
/// A course of one Degree. Code, credit total, type, and Degree are required; the name need not be unique, the code must be unique
/// across all live courses. Theory and lab credit hours are each optional, each between zero and the total, and sum to the total
/// when both are given. The optional prerequisite is a different active course of the same Degree and may not form a cycle. A
/// Degree change may not leave this course's prerequisite, or an active course that depends on it, in another Degree, and a course
/// that an active course depends on cannot be deactivated. Courses are deactivated, never deleted. Nothing outside the catalog changes.
/// </summary>
public sealed class CourseResource()
    : ConfigurationResource<Course>(
        "courses", "Course", "course",
        ConfigurationFields.Code | ConfigurationFields.Type | ConfigurationFields.Degree | ConfigurationFields.CourseCredits | ConfigurationFields.Prerequisite)
{
    public const decimal MaximumCreditHours = 999_999.99m;

    /// <summary>A guard against a corrupt chain looping forever; a real prerequisite chain is far shorter.</summary>
    private const int MaximumChainLength = 1000;

    public override bool SupportsDelete => false;

    protected override bool NamesAreUnique => false;

    // Courses are listed by code, which every course has and no two share.
    protected override string SortProperty => nameof(Course.NormalizedCode);

    protected override string SortKey(Course entity) => entity.NormalizedCode!;

    protected override Course NewRecord() => new();

    protected override Expression<Func<Course, bool>>? ParentFilter(Guid parentId) => course => course.DegreeId == parentId;

    protected override Expression<Func<Course, bool>>? TypeFilter(string type, ConfigurationErrors errors)
    {
        if (TryParseType<CourseType>(type, out var value))
        {
            return course => course.Type == value;
        }

        errors.Add("type", "The type must be Mandatory or Elective.");
        return null;
    }

    protected override Task<bool> CodeTakenAsync(ConfigurationContext context, Course entity, string normalizedCode, CancellationToken cancellationToken) =>
        context.Store.CodeExistsAsync<Course>(normalizedCode, entity.Id, cancellationToken);

    protected override async Task ApplyAsync(
        Course entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Code))
        {
            errors.Add("code", "Enter a course code.");
        }

        if (string.IsNullOrWhiteSpace(input.Type))
        {
            errors.Add("type", "Select a type: Mandatory or Elective.");
        }
        else if (Enum.TryParse<CourseType>(input.Type.Trim(), ignoreCase: true, out var type) && Enum.IsDefined(type)
            && !int.TryParse(input.Type, out _))
        {
            entity.Type = type;
        }
        else
        {
            errors.Add("type", "The type must be Mandatory or Elective.");
        }

        ApplyCredits(entity, input, errors);

        var previousDegree = entity.DegreeId;
        entity.DegreeId = await ValidateReferenceAsync<Degree>(
            input.DegreeId, entity.DegreeId, isNew, "degreeId", "degree", context, errors, cancellationToken);

        // Courses that list this one as their prerequisite must stay in its Degree.
        if (!isNew && !errors.Has("degreeId") && entity.DegreeId != previousDegree
            && await context.Store.HasActiveDependentCoursesAsync(entity.Id, entity.DegreeId, cancellationToken))
        {
            errors.Add("degreeId", "Active courses in the current degree have this course as their prerequisite. Change their prerequisite first.");
        }

        var becomesActive = input.IsActive ?? (isNew || entity.IsActive);
        await ApplyPrerequisiteAsync(entity, input.PrerequisiteCourseId, becomesActive, context, errors, cancellationToken);
    }

    private static void ApplyCredits(Course entity, ConfigurationInput input, ConfigurationErrors errors)
    {
        if (input.CreditHours is not { } total)
        {
            errors.Add("creditHours", "Enter the credit hours.");
        }
        else if (total <= 0 || total > MaximumCreditHours || Scale(total) > 2)
        {
            errors.Add("creditHours", "Enter positive credit hours with no more than two decimal places.");
        }
        else
        {
            entity.CreditHours = total;
        }

        var totalValid = !errors.Has("creditHours");
        decimal? Part(decimal? value, string field, string label)
        {
            if (value is not { } part)
            {
                return null;
            }

            if (part < 0 || Scale(part) > 2)
            {
                errors.Add(field, $"Enter {label} credit hours of zero or more, with no more than two decimal places.");
            }
            else if (totalValid && part > input.CreditHours)
            {
                errors.Add(field, $"The {label} credit hours cannot be more than the total credit hours.");
            }

            return part;
        }

        entity.TheoryCreditHours = Part(input.TheoryCreditHours, "theoryCreditHours", "theory");
        entity.LabCreditHours = Part(input.LabCreditHours, "labCreditHours", "lab");
        if (totalValid && entity.TheoryCreditHours is { } theory && entity.LabCreditHours is { } lab
            && !errors.Has("theoryCreditHours") && !errors.Has("labCreditHours") && theory + lab != input.CreditHours)
        {
            errors.Add("labCreditHours", "Theory and lab credit hours must add up to the total credit hours.");
        }
    }

    private static async Task ApplyPrerequisiteAsync(
        Course entity, Guid? requested, bool becomesActive, ConfigurationContext context, ConfigurationErrors errors, CancellationToken cancellationToken)
    {
        if (requested is not { } prerequisiteId || prerequisiteId == Guid.Empty)
        {
            entity.PrerequisiteCourseId = null;
            return;
        }

        entity.PrerequisiteCourseId = prerequisiteId;
        if (prerequisiteId == entity.Id)
        {
            errors.Add("prerequisiteCourseId", "A course cannot be its own prerequisite.");
            return;
        }

        var prerequisite = await context.Store.FindAsync<Course>(prerequisiteId, cancellationToken);
        if (prerequisite is null)
        {
            errors.Add("prerequisiteCourseId", "The selected prerequisite course does not exist.");
            return;
        }

        if (becomesActive && !prerequisite.IsActive)
        {
            errors.Add("prerequisiteCourseId", "The selected prerequisite course is inactive. Choose an active course.");
        }

        if (!errors.Has("degreeId") && prerequisite.DegreeId != entity.DegreeId)
        {
            errors.Add("prerequisiteCourseId", "The prerequisite must be a course of the same degree.");
        }

        // Walk up the prerequisite's own chain, one link at a time; reaching this course again would close a cycle.
        var seen = new HashSet<Guid>();
        for (Guid? next = prerequisiteId; next is { } current && seen.Count < MaximumChainLength; next = await context.Store.PrerequisiteOfAsync(current, cancellationToken))
        {
            if (current == entity.Id || !seen.Add(current))
            {
                errors.Add("prerequisiteCourseId", "This prerequisite would create a cycle: it already depends on this course.");
                return;
            }
        }
    }

    protected override async Task<string?> UpdateConflictAsync(Course entity, bool wasActive, ConfigurationContext context, CancellationToken cancellationToken) =>
        wasActive && !entity.IsActive && await context.Store.HasActiveDependentCoursesAsync(entity.Id, null, cancellationToken)
            ? "Active courses have this course as their prerequisite, so it cannot be deactivated. Change or deactivate those courses first."
            : null;

    protected override void Describe(Course entity, Dictionary<string, object?> values)
    {
        values["creditHours"] = entity.CreditHours;
        values["theoryCreditHours"] = entity.TheoryCreditHours;
        values["labCreditHours"] = entity.LabCreditHours;
        values["type"] = entity.Type.ToString();
        values["degreeId"] = entity.DegreeId;
        values["prerequisiteCourseId"] = entity.PrerequisiteCourseId;
    }

    protected override ConfigurationItem Project(Course entity, ConfigurationItem item) => item with
    {
        CreditHours = entity.CreditHours,
        TheoryCreditHours = entity.TheoryCreditHours,
        LabCreditHours = entity.LabCreditHours,
        Type = entity.Type.ToString(),
        DegreeId = entity.DegreeId,
        PrerequisiteCourseId = entity.PrerequisiteCourseId,
    };

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7f;
}

/// <summary>A District of one active Division, unique by name within that Division.</summary>
public sealed class DistrictResource()
    : ConfigurationResource<District>("districts", "District", "district", ConfigurationFields.Division)
{
    protected override District NewRecord() => new();

    protected override string DuplicateNameMessage => "A district with this name already exists in this division.";

    protected override Expression<Func<District, bool>> InScope(District candidate)
    {
        var divisionId = candidate.DivisionId;
        return district => district.DivisionId == divisionId;
    }

    protected override Expression<Func<District, bool>>? ParentFilter(Guid parentId) => district => district.DivisionId == parentId;

    protected override async Task<bool> HasDependentsAsync(District entity, ConfigurationContext context, CancellationToken cancellationToken) =>
        await context.Store.ExistsAsync<Upazila>(upazila => upazila.DistrictId == entity.Id, cancellationToken)
        || await context.Store.ExistsAsync<CityCorporation>(city => city.DistrictId == entity.Id, cancellationToken);

    protected override async Task ApplyAsync(
        District entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken) =>
        entity.DivisionId = await ValidateReferenceAsync<Division>(
            input.DivisionId, entity.DivisionId, isNew, "divisionId", "division", context, errors, cancellationToken);

    protected override void Describe(District entity, Dictionary<string, object?> values) => values["divisionId"] = entity.DivisionId;

    protected override ConfigurationItem Project(District entity, ConfigurationItem item) => item with { DivisionId = entity.DivisionId };
}

/// <summary>An Upazila of one active District, unique by name within that District.</summary>
public sealed class UpazilaResource()
    : ConfigurationResource<Upazila>("upazilas", "Upazila", "upazila", ConfigurationFields.District)
{
    protected override Upazila NewRecord() => new();

    protected override string DuplicateNameMessage => "An upazila with this name already exists in this district.";

    protected override Expression<Func<Upazila, bool>> InScope(Upazila candidate)
    {
        var districtId = candidate.DistrictId;
        return upazila => upazila.DistrictId == districtId;
    }

    protected override Expression<Func<Upazila, bool>>? ParentFilter(Guid parentId) => upazila => upazila.DistrictId == parentId;

    protected override async Task ApplyAsync(
        Upazila entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken) =>
        entity.DistrictId = await ValidateReferenceAsync<District>(
            input.DistrictId, entity.DistrictId, isNew, "districtId", "district", context, errors, cancellationToken);

    protected override Task<bool> HasDependentsAsync(Upazila entity, ConfigurationContext context, CancellationToken cancellationToken) =>
        context.Store.UsedByStudentsAsync<Upazila>(entity.Id, cancellationToken);

    protected override void Describe(Upazila entity, Dictionary<string, object?> values) => values["districtId"] = entity.DistrictId;

    protected override ConfigurationItem Project(Upazila entity, ConfigurationItem item) => item with { DistrictId = entity.DistrictId };
}

/// <summary>A City Corporation of one active District, unique by name among the City Corporations of that District. No code.</summary>
public sealed class CityCorporationResource()
    : ConfigurationResource<CityCorporation>("city-corporations", "City corporation", "city-corporation", ConfigurationFields.District)
{
    protected override CityCorporation NewRecord() => new();

    protected override string DuplicateNameMessage => "A city corporation with this name already exists in this district.";

    protected override Expression<Func<CityCorporation, bool>> InScope(CityCorporation candidate)
    {
        var districtId = candidate.DistrictId;
        return city => city.DistrictId == districtId;
    }

    protected override Expression<Func<CityCorporation, bool>>? ParentFilter(Guid parentId) => city => city.DistrictId == parentId;

    protected override async Task ApplyAsync(
        CityCorporation entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken) =>
        entity.DistrictId = await ValidateReferenceAsync<District>(
            input.DistrictId, entity.DistrictId, isNew, "districtId", "district", context, errors, cancellationToken);

    protected override void Describe(CityCorporation entity, Dictionary<string, object?> values) => values["districtId"] = entity.DistrictId;

    protected override ConfigurationItem Project(CityCorporation entity, ConfigurationItem item) => item with { DistrictId = entity.DistrictId };
}
