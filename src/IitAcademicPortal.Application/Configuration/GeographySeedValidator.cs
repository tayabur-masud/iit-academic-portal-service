using System.Text.Json;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Configuration;

public sealed record GeographyIssue(string Path, string Message);

/// <summary>What validating a seed document found. Nothing is written to produce it.</summary>
public sealed record GeographyReport(
    bool IsValid,
    string? SourceCitation,
    string? SourceVersion,
    int Divisions,
    int Districts,
    int Upazilas,
    int CityCorporations,
    bool TargetEmpty,
    IReadOnlyList<GeographyIssue> Issues)
{
    /// <summary>True when the document is valid and the geography tables are empty, so an import would be accepted.</summary>
    public bool CanImport => IsValid && TargetEmpty;
}

/// <summary>The checked hierarchy of a valid seed document: names only, with parents given by nesting.</summary>
public sealed record GeographySeed(
    string SourceCitation,
    string SourceVersion,
    IReadOnlyList<SeedDivision> Divisions);

public sealed record SeedDivision(string Name, IReadOnlyList<SeedDistrict> Districts);

public sealed record SeedDistrict(string Name, IReadOnlyList<SeedUpazila> Upazilas, IReadOnlyList<SeedCityCorporation> CityCorporations);

public sealed record SeedUpazila(string Name);

public sealed record SeedCityCorporation(string Name);

/// <summary>
/// Validates a geography seed document in full before anything is written. It enforces the structure in
/// <c>contracts/geographic-seed.schema.json</c> (required source citation and version, nested names only, no codes or
/// other fields) and the semantic rules: normalized names are unique among Divisions, within each Division's
/// Districts, within each District's Upazilas, and within each District's optional City Corporations. Every problem is
/// reported with its path.
/// </summary>
public static class GeographySeedValidator
{
    public const int MaxIssues = 200;

    public static (GeographySeed? Seed, GeographyReport Report) Validate(JsonElement document, bool targetEmpty = true)
    {
        var issues = new List<GeographyIssue>();
        void Issue(string path, string message)
        {
            if (issues.Count < MaxIssues)
            {
                issues.Add(new GeographyIssue(path, message));
            }
        }

        string? citation = null;
        string? version = null;
        var divisions = new List<SeedDivision>();
        int districtCount = 0, upazilaCount = 0, cityCorporationCount = 0;

        if (document.ValueKind != JsonValueKind.Object)
        {
            Issue("$", "The document must be a JSON object with a source and divisions.");
            return (null, Finish(false));
        }

        RejectUnknown(document, ["source", "divisions"], "$", Issue);

        if (document.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
        {
            RejectUnknown(source, ["citation", "version"], "source", Issue);
            citation = RequiredText(source, "citation", "source.citation", Issue);
            version = RequiredText(source, "version", "source.version", Issue);
        }
        else
        {
            Issue("source", "A source with a citation and version is required.");
        }

        if (!document.TryGetProperty("divisions", out var divisionList) || divisionList.ValueKind != JsonValueKind.Array)
        {
            Issue("divisions", "A list of divisions is required.");
        }
        else if (divisionList.GetArrayLength() == 0)
        {
            Issue("divisions", "At least one division is required.");
        }
        else
        {
            var seenDivisions = new HashSet<string>(StringComparer.Ordinal);
            var d = 0;
            foreach (var division in divisionList.EnumerateArray())
            {
                var path = $"divisions[{d++}]";
                var districts = new List<SeedDistrict>();
                if (division.ValueKind != JsonValueKind.Object)
                {
                    Issue(path, "A division must be an object with a name and districts.");
                    continue;
                }

                RejectUnknown(division, ["name", "districts"], path, Issue);
                var divisionName = Name(division, $"{path}.name", seenDivisions, "A division with this name appears more than once.", Issue);

                if (!division.TryGetProperty("districts", out var districtList) || districtList.ValueKind != JsonValueKind.Array)
                {
                    Issue($"{path}.districts", "A list of districts is required.");
                }
                else
                {
                    var seenDistricts = new HashSet<string>(StringComparer.Ordinal);
                    var i = 0;
                    foreach (var district in districtList.EnumerateArray())
                    {
                        var districtPath = $"{path}.districts[{i++}]";
                        var upazilas = new List<SeedUpazila>();
                        var cityCorporations = new List<SeedCityCorporation>();
                        if (district.ValueKind != JsonValueKind.Object)
                        {
                            Issue(districtPath, "A district must be an object with a name and upazilas.");
                            continue;
                        }

                        RejectUnknown(district, ["name", "upazilas", "cityCorporations"], districtPath, Issue);
                        var districtName = Name(district, $"{districtPath}.name", seenDistricts, "A district with this name appears more than once in its division.", Issue);

                        if (!district.TryGetProperty("upazilas", out var upazilaList) || upazilaList.ValueKind != JsonValueKind.Array)
                        {
                            Issue($"{districtPath}.upazilas", "A list of upazilas is required.");
                        }
                        else
                        {
                            var seenUpazilas = new HashSet<string>(StringComparer.Ordinal);
                            var u = 0;
                            foreach (var upazila in upazilaList.EnumerateArray())
                            {
                                var upazilaPath = $"{districtPath}.upazilas[{u++}]";
                                if (upazila.ValueKind != JsonValueKind.Object)
                                {
                                    Issue(upazilaPath, "An upazila must be an object with a name.");
                                    continue;
                                }

                                RejectUnknown(upazila, ["name"], upazilaPath, Issue);
                                var upazilaName = Name(upazila, $"{upazilaPath}.name", seenUpazilas, "An upazila with this name appears more than once in its district.", Issue);
                                upazilaCount++;
                                if (upazilaName is not null)
                                {
                                    upazilas.Add(new SeedUpazila(upazilaName));
                                }
                            }
                        }

                        // Optional: a District the source lists no City Corporations for simply omits the list.
                        if (district.TryGetProperty("cityCorporations", out var cityList))
                        {
                            if (cityList.ValueKind != JsonValueKind.Array)
                            {
                                Issue($"{districtPath}.cityCorporations", "City corporations must be a list.");
                            }
                            else
                            {
                                var seenCities = new HashSet<string>(StringComparer.Ordinal);
                                var c = 0;
                                foreach (var city in cityList.EnumerateArray())
                                {
                                    var cityPath = $"{districtPath}.cityCorporations[{c++}]";
                                    if (city.ValueKind != JsonValueKind.Object)
                                    {
                                        Issue(cityPath, "A city corporation must be an object with a name.");
                                        continue;
                                    }

                                    RejectUnknown(city, ["name"], cityPath, Issue);
                                    var cityName = Name(city, $"{cityPath}.name", seenCities, "A city corporation with this name appears more than once in its district.", Issue);
                                    cityCorporationCount++;
                                    if (cityName is not null)
                                    {
                                        cityCorporations.Add(new SeedCityCorporation(cityName));
                                    }
                                }
                            }
                        }

                        districtCount++;
                        if (districtName is not null)
                        {
                            districts.Add(new SeedDistrict(districtName, upazilas, cityCorporations));
                        }
                    }
                }

                if (divisionName is not null)
                {
                    divisions.Add(new SeedDivision(divisionName, districts));
                }
            }
        }

        var valid = issues.Count == 0;
        var report = Finish(valid);
        return (valid ? new GeographySeed(citation!, version!, divisions) : null, report);

        GeographyReport Finish(bool isValid) => new(
            isValid, citation, version, divisions.Count, districtCount, upazilaCount, cityCorporationCount, targetEmpty, issues);
    }

    private static void RejectUnknown(JsonElement element, string[] allowed, string path, Action<string, string> issue)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                issue($"{path}.{property.Name}", $"\"{property.Name}\" is not allowed here. Geographic records have names and parents only, with no codes.");
            }
        }
    }

    private static string? RequiredText(JsonElement element, string property, string path, Action<string, string> issue)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            issue(path, "A non-empty text value is required.");
            return null;
        }

        return value.GetString()!.Trim();
    }

    private static string? Name(JsonElement element, string path, HashSet<string> seen, string duplicateMessage, Action<string, string> issue)
    {
        var name = RequiredText(element, "name", path, issue);
        if (name is null)
        {
            return null;
        }

        if (name.Length > NameNormalizer.MaxNameLength)
        {
            issue(path, $"The name can be at most {NameNormalizer.MaxNameLength} characters.");
            return null;
        }

        if (!seen.Add(NameNormalizer.Normalize(name)))
        {
            issue(path, duplicateMessage);
            return null;
        }

        return name;
    }
}
