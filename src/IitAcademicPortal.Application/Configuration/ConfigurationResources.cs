using System.Linq.Expressions;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Configuration;

/// <summary>A catalog with only a name and an active flag.</summary>
public sealed class NameOnlyResource<T>(string route, string displayName, string eventKey, Func<T> create)
    : ConfigurationResource<T>(route, displayName, eventKey, ConfigurationFields.None) where T : ConfigurationRecord
{
    protected override T NewRecord() => create();
}

/// <summary>A catalog with an optional code that is unique within the catalog.</summary>
public class CodedResource<T>(string route, string displayName, string eventKey, Func<T> create, ConfigurationFields extra = ConfigurationFields.None)
    : ConfigurationResource<T>(route, displayName, eventKey, ConfigurationFields.Code | extra) where T : ConfigurationRecord, ICodedRecord
{
    protected override T NewRecord() => create();

    protected override Task<bool> CodeTakenAsync(ConfigurationContext context, T entity, string normalizedCode, CancellationToken cancellationToken) =>
        context.Store.CodeExistsAsync<T>(normalizedCode, entity.Id, cancellationToken);
}

/// <summary>A school, college, or university: a fixed type plus an optional code and address.</summary>
public sealed class InstitutionResource()
    : CodedResource<EducationalInstitution>(
        "institutions", "Educational institution", "institution", () => new EducationalInstitution(),
        ConfigurationFields.Type | ConfigurationFields.Address)
{
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

        var address = NameNormalizer.DisplayCode(input.Address);
        if (address is { Length: > NameNormalizer.MaxAddressLength })
        {
            errors.Add("address", $"The address can be at most {NameNormalizer.MaxAddressLength} characters.");
        }
        else
        {
            entity.Address = address;
        }

        return Task.CompletedTask;
    }

    protected override void Describe(EducationalInstitution entity, Dictionary<string, object?> values)
    {
        values["type"] = entity.Type.ToString();
        values["address"] = entity.Address;
    }

    protected override ConfigurationItem Project(EducationalInstitution entity, ConfigurationItem item) =>
        item with { Type = entity.Type.ToString(), Address = entity.Address };
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

    protected override void Describe(Batch entity, Dictionary<string, object?> values)
    {
        values["degreeId"] = entity.DegreeId;
        values["startingSemesterId"] = entity.StartingSemesterId;
        values["academicSessionId"] = entity.AcademicSessionId;
    }

    protected override ConfigurationItem Project(Batch entity, ConfigurationItem item) =>
        item with { DegreeId = entity.DegreeId, StartingSemesterId = entity.StartingSemesterId, AcademicSessionId = entity.AcademicSessionId };
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

    protected override void Describe(Upazila entity, Dictionary<string, object?> values) => values["districtId"] = entity.DistrictId;

    protected override ConfigurationItem Project(Upazila entity, ConfigurationItem item) => item with { DistrictId = entity.DistrictId };
}
