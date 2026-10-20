using System.Text.Json;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Configuration;

public enum GeographyImportOutcome
{
    Imported,
    Valid,
    Invalid,
    TargetNotEmpty,
}

public sealed record GeographyImportResult(GeographyImportOutcome Outcome, GeographyReport Report);

/// <summary>
/// Loads the owner-approved geography document. Validation never writes. An import validates the whole document
/// again, requires the geography tables to be empty (so Admin-managed rows are never overwritten), and then adds the
/// complete hierarchy together with one audit event in a single transaction: all of it, or none.
/// </summary>
public sealed class GeographyImportService(IConfigurationStore store, IAuditEventRecorder recorder)
{
    private const int MaxCitationInAudit = 200;

    public async Task<GeographyImportResult> ValidateAsync(JsonElement document, CancellationToken cancellationToken)
    {
        var targetEmpty = await IsTargetEmptyAsync(cancellationToken);
        var (seed, report) = GeographySeedValidator.Validate(document, targetEmpty);
        return new GeographyImportResult(seed is null ? GeographyImportOutcome.Invalid : GeographyImportOutcome.Valid, report);
    }

    public async Task<GeographyImportResult> ImportAsync(JsonElement document, CancellationToken cancellationToken)
    {
        var targetEmpty = await IsTargetEmptyAsync(cancellationToken);
        var (seed, report) = GeographySeedValidator.Validate(document, targetEmpty);
        if (seed is null)
        {
            return new GeographyImportResult(GeographyImportOutcome.Invalid, report);
        }

        if (!targetEmpty)
        {
            return new GeographyImportResult(GeographyImportOutcome.TargetNotEmpty, report);
        }

        var divisions = new List<Division>();
        var districts = new List<District>();
        var upazilas = new List<Upazila>();
        var cityCorporations = new List<CityCorporation>();
        foreach (var seedDivision in seed.Divisions)
        {
            var division = new Division();
            division.Rename(seedDivision.Name);
            divisions.Add(division);
            foreach (var seedDistrict in seedDivision.Districts)
            {
                var district = new District { DivisionId = division.Id };
                district.Rename(seedDistrict.Name);
                districts.Add(district);
                foreach (var seedUpazila in seedDistrict.Upazilas)
                {
                    var upazila = new Upazila { DistrictId = district.Id };
                    upazila.Rename(seedUpazila.Name);
                    upazilas.Add(upazila);
                }

                foreach (var seedCity in seedDistrict.CityCorporations)
                {
                    var city = new CityCorporation { DistrictId = district.Id };
                    city.Rename(seedCity.Name);
                    cityCorporations.Add(city);
                }
            }
        }

        store.AddRange(divisions);
        store.AddRange(districts);
        store.AddRange(upazilas);
        store.AddRange(cityCorporations);
        recorder.Stage(new AuditEventRequest(ConfigurationAuditEventDefinitions.GeographyImported, AuditOutcome.Success)
        {
            EntityType = "Geography",
            Metadata = new Dictionary<string, object?>
            {
                ["sourceCitation"] = seed.SourceCitation.Length > MaxCitationInAudit ? seed.SourceCitation[..MaxCitationInAudit] : seed.SourceCitation,
                ["sourceVersion"] = seed.SourceVersion,
                ["divisions"] = divisions.Count,
                ["districts"] = districts.Count,
                ["upazilas"] = upazilas.Count,
                ["cityCorporations"] = cityCorporations.Count,
            },
        });

        try
        {
            await store.SaveAsync(cancellationToken);
        }
        catch (ConfigurationConflictException)
        {
            // Another import or edit got there first: nothing was committed, so report the tables as not empty.
            return new GeographyImportResult(GeographyImportOutcome.TargetNotEmpty, report with { TargetEmpty = false });
        }

        return new GeographyImportResult(GeographyImportOutcome.Imported, report);
    }

    private async Task<bool> IsTargetEmptyAsync(CancellationToken cancellationToken) =>
        await store.CountAsync<Division>(cancellationToken) == 0
        && await store.CountAsync<District>(cancellationToken) == 0
        && await store.CountAsync<Upazila>(cancellationToken) == 0
        && await store.CountAsync<CityCorporation>(cancellationToken) == 0;
}
