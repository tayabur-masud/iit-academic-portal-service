using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.UserManagement;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Students;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Students;

/// <summary>
/// A notification-capable API host, an active-Admin client, seeded Configuration references (two degrees with batches, halls, and a
/// small geography), and helpers for the Student Management routes.
/// </summary>
public abstract class StudentTestBase : UserManagementTestBase
{
    protected const string Students = "/api/admin/students";

    /// <summary>The Configuration records the tests choose from. "Pgdit" and "Emit" are two programs with one batch each.</summary>
    protected sealed record References(
        Guid Pgdit, Guid PgditBatch, Guid Emit, Guid EmitBatch, Guid EmitSecondBatch, Guid InactiveDegree, Guid InactiveDegreeBatch,
        Guid Hall, Guid OtherHall, Guid InactiveHall, Guid Division, Guid District, Guid Upazila, Guid OtherUpazila, Guid InactiveUpazila);

    private References? references;

    protected record Admission(Guid Id, Guid ProfileId, string UserId, string Email);

    protected async Task<References> RefsAsync()
    {
        if (references is not null)
        {
            return references;
        }

        T Named<T>(T record, string name, bool active = true) where T : ConfigurationRecord
        {
            record.Rename(name);
            record.IsActive = active;
            return record;
        }

        Degree NewDegree(string name, decimal credits, bool active = true)
        {
            var degree = Named(new Degree(), name, active);
            degree.SetCreditsToBeCompleted(credits);
            return degree;
        }

        var pgdit = NewDegree("PGDIT", 36);
        var emit = NewDegree("EMIT", 48);
        var inactiveDegree = NewDegree("Retired Program", 30, active: false);
        var semester = Named(new Semester(), "Spring");
        var session = Named(new AcademicSession(), "2026-2027");
        Batch NewBatch(Degree degree, string name) =>
            Named(new Batch { DegreeId = degree.Id, StartingSemesterId = semester.Id, AcademicSessionId = session.Id }, name);
        var pgditBatch = NewBatch(pgdit, "PGDIT Batch 1");
        var emitBatch = NewBatch(emit, "EMIT Batch 7");
        var emitSecond = NewBatch(emit, "EMIT Batch 8");
        var inactiveDegreeBatch = NewBatch(inactiveDegree, "Retired Batch");
        var hall = Named(new Hall(), "Jagannath Hall");
        var otherHall = Named(new Hall(), "Salimullah Muslim Hall");
        var inactiveHall = Named(new Hall(), "Closed Hall", active: false);
        var division = Named(new Division(), "Dhaka");
        var district = Named(new District { DivisionId = division.Id }, "Gazipur");
        var upazila = Named(new Upazila { DistrictId = district.Id }, "Kaliakair");
        var otherUpazila = Named(new Upazila { DistrictId = district.Id }, "Sreepur");
        var inactiveUpazila = Named(new Upazila { DistrictId = district.Id }, "Old Thana", active: false);

        await Host.WithDbAsync(async db =>
        {
            db.AddRange(pgdit, emit, inactiveDegree, semester, session);
            await db.SaveChangesAsync();
            db.AddRange(pgditBatch, emitBatch, emitSecond, inactiveDegreeBatch, hall, otherHall, inactiveHall, division);
            await db.SaveChangesAsync();
            db.Add(district);
            await db.SaveChangesAsync();
            db.AddRange(upazila, otherUpazila, inactiveUpazila);
            await db.SaveChangesAsync();
        });

        references = new References(
            pgdit.Id, pgditBatch.Id, emit.Id, emitBatch.Id, emitSecond.Id, inactiveDegree.Id, inactiveDegreeBatch.Id, hall.Id, otherHall.Id,
            inactiveHall.Id, division.Id, district.Id, upazila.Id, otherUpazila.Id, inactiveUpazila.Id);
        return references;
    }

    /// <summary>A complete admission form for a degree and batch; change entries to vary it.</summary>
    protected static Dictionary<string, object?> Form(string? email, Guid degreeId, Guid batchId, string name = "Ayesha Rahman") => new()
    {
        ["email"] = email,
        ["applicantName"] = name,
        ["fatherName"] = "Abdur Rahman",
        ["motherName"] = "Salma Begum",
        ["dateOfBirth"] = "1998-04-12",
        ["placeOfBirth"] = "Dhaka",
        ["maritalStatus"] = "Single",
        ["nationality"] = "Bangladeshi",
        ["applicantMobile"] = "+8801711000000",
        ["mailingAddress"] = null,
        ["permanentAddress"] = null,
        ["guardianName"] = null,
        ["guardianRelationship"] = null,
        ["guardianAddress"] = null,
        ["guardianCellNumber"] = null,
        ["degreeId"] = degreeId,
        ["batchId"] = batchId,
        ["admissionSerialNumber"] = "SER-" + Guid.NewGuid().ToString("N")[..6],
        ["admissionExamRoll"] = "ROLL-" + Guid.NewGuid().ToString("N")[..6],
        ["classRoll"] = null,
        ["assignedHallId"] = null,
        ["registrationNumber"] = null,
        ["academicHistory"] = Array.Empty<object>(),
    };

    /// <summary>The same form as an update body (no email).</summary>
    protected static Dictionary<string, object?> UpdateForm(Dictionary<string, object?> form)
    {
        var copy = new Dictionary<string, object?>(form);
        copy.Remove("email");
        return copy;
    }

    /// <summary>The current admission as an update body, so a test changes only what it means to.</summary>
    protected static Dictionary<string, object?> FormOf(JsonElement admission)
    {
        var profile = admission.GetProperty("profile");
        string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        object? Address(string name) => profile.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Object
            ? new { details = Text(a, "details"), upazilaId = Text(a, "upazilaId"), cellNumber = Text(a, "cellNumber") }
            : null;
        return new Dictionary<string, object?>
        {
            ["applicantName"] = Text(profile, "applicantName"),
            ["fatherName"] = Text(profile, "fatherName"),
            ["motherName"] = Text(profile, "motherName"),
            ["dateOfBirth"] = Text(profile, "dateOfBirth"),
            ["placeOfBirth"] = Text(profile, "placeOfBirth"),
            ["maritalStatus"] = Text(profile, "maritalStatus"),
            ["nationality"] = Text(profile, "nationality"),
            ["applicantMobile"] = Text(profile, "applicantMobile"),
            ["mailingAddress"] = Address("mailingAddress"),
            ["permanentAddress"] = Address("permanentAddress"),
            ["guardianName"] = Text(profile, "guardianName"),
            ["guardianRelationship"] = Text(profile, "guardianRelationship"),
            ["guardianAddress"] = Text(profile, "guardianAddress"),
            ["guardianCellNumber"] = Text(profile, "guardianCellNumber"),
            ["degreeId"] = Text(admission, "degreeId"),
            ["batchId"] = Text(admission, "batchId"),
            ["admissionSerialNumber"] = Text(admission, "admissionSerialNumber"),
            ["admissionExamRoll"] = Text(admission, "admissionExamRoll"),
            ["classRoll"] = Text(admission, "classRoll"),
            ["assignedHallId"] = Text(admission, "assignedHallId"),
            ["registrationNumber"] = Text(admission, "registrationNumber"),
            ["academicHistory"] = profile.GetProperty("academicHistory").EnumerateArray().Select(h => (object)new Dictionary<string, object?>
            {
                ["id"] = Text(h, "id"),
                ["category"] = Text(h, "category"),
                ["examination"] = Text(h, "examination"),
                ["schoolOrCollege"] = Text(h, "schoolOrCollege"),
                ["board"] = Text(h, "board"),
                ["group"] = Text(h, "group"),
                ["department"] = Text(h, "department"),
                ["university"] = Text(h, "university"),
                ["subject"] = Text(h, "subject"),
                ["institution"] = Text(h, "institution"),
                ["year"] = Text(h, "year"),
                ["marksObtained"] = Text(h, "marksObtained"),
                ["marksPercentage"] = Text(h, "marksPercentage"),
                ["cgpa"] = Text(h, "cgpa"),
                ["class"] = Text(h, "class"),
                ["division"] = Text(h, "division"),
            }).ToArray(),
        };
    }

    protected async Task<(HttpStatusCode Status, JsonElement Body)> CreateAdmission(PortalClient admin, Dictionary<string, object?> form) =>
        await SendAsync(admin, HttpMethod.Post, Students, form);

    /// <summary>Creates an admission and returns its identifiers, failing the test if it was not created.</summary>
    protected async Task<Admission> AdmitAsync(PortalClient admin, Dictionary<string, object?> form)
    {
        var (status, body) = await CreateAdmission(admin, form);
        Assert.True(status == HttpStatusCode.Created, $"Creating an admission returned {status}: {body}");
        var admission = body.GetProperty("admission");
        var profile = admission.GetProperty("profile");
        return new Admission(
            admission.GetProperty("id").GetGuid(), profile.GetProperty("id").GetGuid(), profile.GetProperty("userId").GetString()!,
            profile.GetProperty("email").GetString()!);
    }

    protected static Task<(HttpStatusCode Status, JsonElement Body)> GetAdmission(PortalClient admin, Guid id) =>
        SendAsync(admin, HttpMethod.Get, $"{Students}/{id}");

    protected static Task<(HttpStatusCode Status, JsonElement Body)> PutAdmission(PortalClient admin, Guid id, Dictionary<string, object?> form) =>
        SendAsync(admin, HttpMethod.Put, $"{Students}/{id}", form);

    protected async Task<(int Profiles, int Admissions, int Users)> CountsAsync()
    {
        var counts = (0, 0, 0);
        await Host.WithDbAsync(async db =>
            counts = (await db.StudentProfiles.CountAsync(), await db.StudentAdmissions.CountAsync(), await db.Users.CountAsync()));
        return counts;
    }

    protected async Task<StudentAdmissionRecord> AdmissionRowAsync(Guid id)
    {
        StudentAdmissionRecord? row = null;
        await Host.WithDbAsync(async db => row = await db.StudentAdmissions.AsNoTracking().SingleAsync(a => a.Id == id));
        return row!;
    }

    protected static string Unique(string prefix) => PortalApiFactory.UniqueEmail(prefix);
}
