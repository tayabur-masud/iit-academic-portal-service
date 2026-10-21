using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Students;

/// <summary>
/// US1: an active Admin enters admissions. One Student profile and account per person, one admission per Degree and Batch, profile
/// academic history kept as text snapshots, structured addresses, and no access for anyone else.
/// </summary>
public sealed class StudentAdmissionTests : StudentTestBase
{
    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task A_first_admission_creates_one_pending_Student_account_profile_and_admission_and_requests_setup_once()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("applicant");
        var form = Form($"  {email.ToUpperInvariant()} ", refs.Pgdit, refs.PgditBatch);
        form["classRoll"] = "17";

        var (status, body) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(body.GetProperty("accountCreated").GetBoolean());
        Assert.True(body.GetProperty("profileCreated").GetBoolean());
        Assert.True(body.GetProperty("setupEmailRequested").GetBoolean());
        var admission = body.GetProperty("admission");
        var profile = admission.GetProperty("profile");
        Assert.Equal("Ayesha Rahman", profile.GetProperty("applicantName").GetString());
        Assert.Equal("+8801711000000", profile.GetProperty("applicantMobile").GetString());
        Assert.Equal("1998-04-12", profile.GetProperty("dateOfBirth").GetString());
        Assert.Equal("PendingCredentialSetup", profile.GetProperty("accountStatus").GetString());
        Assert.Equal("PGDIT", admission.GetProperty("degreeName").GetString());
        Assert.Equal("PGDIT Batch 1", admission.GetProperty("batchName").GetString());
        Assert.Equal(form["admissionSerialNumber"], admission.GetProperty("admissionSerialNumber").GetString());
        Assert.Equal(form["admissionExamRoll"], admission.GetProperty("admissionExamRoll").GetString());
        Assert.Equal("17", admission.GetProperty("classRoll").GetString());
        Assert.False(admission.TryGetProperty("assignedHallId", out _));
        Assert.False(admission.TryGetProperty("registrationNumber", out _));
        Assert.Equal(AdminUserId, admission.GetProperty("createdByUserId").GetString());

        var userId = profile.GetProperty("userId").GetString()!;
        var user = await UserAsync(userId);
        Assert.Equal(email.ToUpperInvariant(), user.NormalizedEmail);
        Assert.Null(user.PasswordHash);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, user.AccountStatus);
        Assert.Equal(PortalRoles.Student, user.DefaultRole);
        Assert.Equal([PortalRoles.Student], await AssignedRolesAsync(userId));

        var setup = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationEventType.AccountCredentialSetup, setup.EventType);
        Assert.Equal(userId, setup.RecipientUserId);
        var text = body.GetRawText();
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proof", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_returning_Student_gets_a_distinct_admission_under_the_same_profile_and_account_without_another_setup_email()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("returning");
        var pgdit = await AdmitAsync(admin, Form(email, refs.Pgdit, refs.PgditBatch));

        var (status, body) = await CreateAdmission(admin, Form(email.ToUpperInvariant(), refs.Emit, refs.EmitBatch));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(body.GetProperty("accountCreated").GetBoolean());
        Assert.False(body.GetProperty("profileCreated").GetBoolean());
        Assert.False(body.GetProperty("setupEmailRequested").GetBoolean());
        var emit = body.GetProperty("admission");
        Assert.NotEqual(pgdit.Id, emit.GetProperty("id").GetGuid());
        Assert.Equal(pgdit.ProfileId, emit.GetProperty("profile").GetProperty("id").GetGuid());
        Assert.Equal(pgdit.UserId, emit.GetProperty("profile").GetProperty("userId").GetString());
        var other = Assert.Single(emit.GetProperty("otherAdmissions").EnumerateArray());
        Assert.Equal(pgdit.Id, other.GetProperty("id").GetGuid());
        Assert.Equal("PGDIT", other.GetProperty("degreeName").GetString());

        Assert.Equal((1, 2), await ProfilesAndAdmissionsOfAsync(pgdit.UserId));
        Assert.Single(await Host.RecordsAsync(), r => r.EventType == NotificationEventType.AccountCredentialSetup);
        Assert.Single(await EventsAsync("account.created"), e => e.EntityId == pgdit.UserId);
    }

    [Fact]
    public async Task The_same_Degree_and_Batch_twice_for_one_Student_is_a_409_and_writes_nothing()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("twice");
        await AdmitAsync(admin, Form(email, refs.Emit, refs.EmitBatch));
        var before = await CountsAsync();

        var (status, problem) = await CreateAdmission(admin, Form(email, refs.Emit, refs.EmitBatch, name: "Changed Name"));
        var (otherBatch, _) = await CreateAdmission(admin, Form(email, refs.Emit, refs.EmitSecondBatch));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already has an admission", problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Created, otherBatch);
        Assert.Equal(before.Admissions + 1, (await CountsAsync()).Admissions);
        await Host.WithDbAsync(async db => Assert.Equal("Ayesha Rahman", (await db.StudentProfiles.SingleAsync()).ApplicantName));
    }

    [Theory]
    [InlineData(PortalRoles.Admin)]
    [InlineData(PortalRoles.Teacher)]
    public async Task An_email_of_a_non_Student_account_is_a_409_and_nothing_is_merged_or_created(string role)
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("staff");
        var userId = await Host.CreateUserAsync(email, role);
        var before = await CountsAsync();

        var (status, problem) = await CreateAdmission(admin, Form(email, refs.Pgdit, refs.PgditBatch));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("not a Student account", problem.GetProperty("detail").GetString());
        Assert.Equal(before, await CountsAsync());
        Assert.Equal([role], await AssignedRolesAsync(userId));
        Assert.Empty(await Host.RecordsAsync());
    }

    [Fact]
    public async Task An_existing_Student_account_without_a_profile_gets_one_without_a_new_account_or_setup_email()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var records = (await Host.RecordsAsync()).Count;

        var (status, body) = await CreateAdmission(admin, Form(account.Email, refs.Pgdit, refs.PgditBatch));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(body.GetProperty("accountCreated").GetBoolean());
        Assert.True(body.GetProperty("profileCreated").GetBoolean());
        Assert.False(body.GetProperty("setupEmailRequested").GetBoolean());
        Assert.Equal(account.UserId, body.GetProperty("admission").GetProperty("profile").GetProperty("userId").GetString());
        Assert.Equal(records, (await Host.RecordsAsync()).Count);
    }

    [Theory]
    [InlineData("email", null)]
    [InlineData("email", "not-an-email")]
    [InlineData("applicantName", "  ")]
    [InlineData("applicantMobile", null)]
    [InlineData("admissionSerialNumber", "")]
    [InlineData("admissionExamRoll", null)]
    [InlineData("degreeId", null)]
    [InlineData("batchId", null)]
    public async Task A_missing_required_field_is_a_field_keyed_400_and_creates_nothing(string field, string? value)
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("missing"), refs.Pgdit, refs.PgditBatch);
        form[field] = value;
        var before = await CountsAsync();

        var (status, problem) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, field), problem.ToString());
        Assert.Equal(before, await CountsAsync());
        Assert.Empty(await Host.RecordsAsync());
    }

    [Fact]
    public async Task Degree_and_batch_must_exist_be_active_and_belong_together()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();

        var (mismatch, mismatchProblem) = await CreateAdmission(admin, Form(Unique("a"), refs.Pgdit, refs.EmitBatch));
        var (inactive, inactiveProblem) = await CreateAdmission(admin, Form(Unique("b"), refs.InactiveDegree, refs.InactiveDegreeBatch));
        var (unknown, unknownProblem) = await CreateAdmission(admin, Form(Unique("c"), Guid.NewGuid(), refs.PgditBatch));
        var (hall, hallProblem) = await CreateAdmission(admin, WithValue(Form(Unique("d"), refs.Pgdit, refs.PgditBatch), "assignedHallId", refs.InactiveHall));

        Assert.Equal(HttpStatusCode.BadRequest, mismatch);
        Assert.Contains("does not belong", FirstError(mismatchProblem, "batchId"));
        Assert.Equal(HttpStatusCode.BadRequest, inactive);
        Assert.True(HasError(inactiveProblem, "degreeId"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown);
        Assert.True(HasError(unknownProblem, "degreeId"));
        Assert.Equal(HttpStatusCode.BadRequest, hall);
        Assert.True(HasError(hallProblem, "assignedHallId"));
    }

    [Fact]
    public async Task All_six_history_categories_keep_every_supplied_value_exactly_and_require_none_of_the_others()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("history"), refs.Pgdit, refs.PgditBatch);
        form["academicHistory"] = new object[]
        {
            new { category = "SscOLevel", examination = "S.S.C.", schoolOrCollege = "Motijheel Model High School", board = "Dhaka Board (old name)", group = "Science", year = "2013", marksObtained = "1032", marksPercentage = "86.00", cgpa = "5.00", division = "1st" },
            new { category = "HscALevel", examination = "A-Level", schoolOrCollege = "Unlisted College, Sylhet", board = "Edexcel", group = "Science", year = "2015", cgpa = "4.80" },
            new { category = "BachelorGraduation", university = "University of Dhaka", department = "Computer Science", subject = "CSE", year = "2019", cgpa = "3.71", @class = "1st Class" },
            new { category = "Master", university = "BUET", subject = "EEE", year = "2021" },
            new { category = "Pgdit", institution = "IIT (before the portal)", year = "2022", cgpa = "3.90" },
            new { category = "Other", examination = "Diploma in Networking", institution = "BITM", year = "2020", marksPercentage = "78.5%" },
        };

        var (status, body) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.Created, status);
        var history = body.GetProperty("admission").GetProperty("profile").GetProperty("academicHistory").EnumerateArray().ToList();
        Assert.Equal(["SscOLevel", "HscALevel", "BachelorGraduation", "Master", "Pgdit", "Other"], history.Select(h => h.GetProperty("category").GetString()));
        Assert.Equal("Dhaka Board (old name)", history[0].GetProperty("board").GetString());
        Assert.Equal("86.00", history[0].GetProperty("marksPercentage").GetString());
        Assert.Equal("5.00", history[0].GetProperty("cgpa").GetString());
        Assert.Equal("Unlisted College, Sylhet", history[1].GetProperty("schoolOrCollege").GetString());
        Assert.Equal("1st Class", history[2].GetProperty("class").GetString());
        Assert.Equal(JsonValueKind.Null, history[3].GetProperty("board").ValueKind);
        Assert.Equal("78.5%", history[5].GetProperty("marksPercentage").GetString());
    }

    [Theory]
    [InlineData("Phd")]
    [InlineData("")]
    [InlineData("3")]
    public async Task An_unknown_history_category_is_a_field_keyed_400(string category)
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("cat"), refs.Pgdit, refs.PgditBatch);
        form["academicHistory"] = new object[] { new { category = "Master" }, new { category } };

        var (status, problem) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "academicHistory[1].category"), problem.ToString());
    }

    [Fact]
    public async Task History_on_the_profile_is_shared_by_every_admission_and_an_update_keeps_ids_of_kept_rows()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("shared");
        var form = Form(email, refs.Pgdit, refs.PgditBatch);
        form["academicHistory"] = new object[] { new { category = "SscOLevel", year = "2013" }, new { category = "HscALevel", year = "2015" } };
        var pgdit = await AdmitAsync(admin, form);
        var (_, current) = await GetAdmission(admin, pgdit.Id);
        var update = FormOf(current);
        var rows = (object[])update["academicHistory"]!;
        var keptId = ((Dictionary<string, object?>)rows[1])["id"];
        update["academicHistory"] = new object[] { rows[1], new { category = "BachelorGraduation", university = "DU" } };

        Assert.Equal(HttpStatusCode.OK, (await PutAdmission(admin, pgdit.Id, update)).Status);
        var emit = await AdmitAsync(admin, WithValue(UpdateFormWithEmail(update, email), "degreeId", refs.Emit, "batchId", refs.EmitBatch));
        var (_, fromEmit) = await GetAdmission(admin, emit.Id);

        var history = fromEmit.GetProperty("profile").GetProperty("academicHistory").EnumerateArray().ToList();
        Assert.Equal(["HscALevel", "BachelorGraduation"], history.Select(h => h.GetProperty("category").GetString()));
        Assert.Equal(keptId, history[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Addresses_resolve_their_district_and_division_from_an_upazila()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("address"), refs.Pgdit, refs.PgditBatch);
        form["mailingAddress"] = new { details = "House 12, Road 5, Dhanmondi", districtId = refs.District, upazilaId = refs.Upazila, cellNumber = "01811000000" };
        form["permanentAddress"] = new { details = "Village Chandra", districtId = refs.District, upazilaId = refs.OtherUpazila };

        var (status, body) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.Created, status);
        var mailing = body.GetProperty("admission").GetProperty("profile").GetProperty("mailingAddress");
        Assert.Equal("House 12, Road 5, Dhanmondi", mailing.GetProperty("details").GetString());
        Assert.Equal("Kaliakair", mailing.GetProperty("upazilaName").GetString());
        Assert.Equal(refs.District, mailing.GetProperty("districtId").GetGuid());
        Assert.Equal("Gazipur", mailing.GetProperty("districtName").GetString());
        Assert.Equal(refs.Division, mailing.GetProperty("divisionId").GetGuid());
        Assert.Equal("Dhaka", mailing.GetProperty("divisionName").GetString());
        Assert.Equal("01811000000", mailing.GetProperty("cellNumber").GetString());
        Assert.Equal("Sreepur", body.GetProperty("admission").GetProperty("profile").GetProperty("permanentAddress").GetProperty("upazilaName").GetString());
        Assert.Equal(JsonValueKind.Null, mailing.GetProperty("cityCorporationId").ValueKind);
    }

    [Fact]
    public async Task An_address_may_use_a_city_corporation_whose_district_and_division_are_derived()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("city"), refs.Pgdit, refs.PgditBatch);
        form["mailingAddress"] = new { details = "Holding 7, Tongi", districtId = refs.District, cityCorporationId = refs.CityCorporation };

        var (status, body) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.Created, status);
        var mailing = body.GetProperty("admission").GetProperty("profile").GetProperty("mailingAddress");
        Assert.Equal(refs.CityCorporation, mailing.GetProperty("cityCorporationId").GetGuid());
        Assert.Equal("Gazipur City Corporation", mailing.GetProperty("cityCorporationName").GetString());
        Assert.Equal(JsonValueKind.Null, mailing.GetProperty("upazilaId").ValueKind);
        Assert.Equal(refs.District, mailing.GetProperty("districtId").GetGuid());
        Assert.Equal("Gazipur", mailing.GetProperty("districtName").GetString());
        Assert.Equal("Dhaka", mailing.GetProperty("divisionName").GetString());
    }

    [Fact]
    public async Task Switching_an_address_between_locality_types_stores_only_the_new_one()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("switch"), refs.Pgdit, refs.PgditBatch);
        form["mailingAddress"] = new { details = "House 1", districtId = refs.District, upazilaId = refs.Upazila };
        var admission = await AdmitAsync(admin, form);

        var update = FormOf((await GetAdmission(admin, admission.Id)).Body);
        update["mailingAddress"] = new { details = "House 1", districtId = refs.OtherDistrict, cityCorporationId = refs.OtherDistrictCityCorporation };
        var (status, body) = await PutAdmission(admin, admission.Id, update);

        Assert.True(status == HttpStatusCode.OK, body.ToString());
        var mailing = body.GetProperty("profile").GetProperty("mailingAddress");
        Assert.Equal("Narayanganj City Corporation", mailing.GetProperty("cityCorporationName").GetString());
        Assert.Equal("Narayanganj", mailing.GetProperty("districtName").GetString());
        await Host.WithDbAsync(async db =>
        {
            var profile = await db.StudentProfiles.AsNoTracking().SingleAsync(p => p.Id == admission.ProfileId);
            Assert.Null(profile.MailingUpazilaId);
            Assert.Equal(refs.OtherDistrictCityCorporation, profile.MailingCityCorporationId);
        });
    }

    public static TheoryData<string, string> InvalidAddresses => new()
    {
        { "no-district", "mailingAddress.districtId" },
        { "no-locality", "mailingAddress.upazilaId" },
        { "both-localities", "mailingAddress.cityCorporationId" },
        { "no-details", "mailingAddress.details" },
        { "unknown-upazila", "mailingAddress.upazilaId" },
        { "unknown-city", "mailingAddress.cityCorporationId" },
        { "deleted-city", "mailingAddress.cityCorporationId" },
        { "upazila-of-another-district", "mailingAddress.upazilaId" },
        { "city-of-another-district", "mailingAddress.cityCorporationId" },
        { "inactive-upazila", "mailingAddress.upazilaId" },
        { "inactive-city", "mailingAddress.cityCorporationId" },
        { "district-not-a-district", "mailingAddress.cityCorporationId" },
    };

    [Theory]
    [MemberData(nameof(InvalidAddresses))]
    public async Task An_invalid_address_locality_is_refused_with_its_field_and_nothing_is_stored(string problem, string field)
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        if (problem == "deleted-city")
        {
            await Host.WithDbAsync(async db =>
            {
                (await db.CityCorporations.SingleAsync(c => c.Id == refs.OtherDistrictCityCorporation)).MarkDeleted(DateTime.UtcNow, null);
                await db.SaveChangesAsync();
            });
        }

        object address = problem switch
        {
            "no-district" => new { details = "House 1", upazilaId = refs.Upazila },
            "no-locality" => new { details = "House 1", districtId = refs.District },
            "both-localities" => new { details = "House 1", districtId = refs.District, upazilaId = refs.Upazila, cityCorporationId = refs.CityCorporation },
            "no-details" => new { districtId = refs.District, upazilaId = refs.Upazila },
            "unknown-upazila" => new { details = "House 1", districtId = refs.District, upazilaId = Guid.NewGuid() },
            "unknown-city" => new { details = "House 1", districtId = refs.District, cityCorporationId = Guid.NewGuid() },
            "deleted-city" => new { details = "House 1", districtId = refs.OtherDistrict, cityCorporationId = refs.OtherDistrictCityCorporation },
            "upazila-of-another-district" => new { details = "House 1", districtId = refs.District, upazilaId = refs.OtherDistrictUpazila },
            "city-of-another-district" => new { details = "House 1", districtId = refs.District, cityCorporationId = refs.OtherDistrictCityCorporation },
            "inactive-upazila" => new { details = "House 1", districtId = refs.District, upazilaId = refs.InactiveUpazila },
            "inactive-city" => new { details = "House 1", districtId = refs.District, cityCorporationId = refs.InactiveCityCorporation },
            // A Division id is not a District, so the City Corporation cannot belong to it.
            _ => new { details = "House 1", districtId = refs.Division, cityCorporationId = refs.CityCorporation },
        };
        var form = Form(Unique("addr"), refs.Pgdit, refs.PgditBatch);
        form["mailingAddress"] = address;

        var (status, body) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(body, field), $"Expected an error on {field}: {body}");
        Assert.Equal(0, (await CountsAsync()).Profiles);
    }

    [Fact]
    public async Task An_address_takes_no_division_id()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("division"), refs.Pgdit, refs.PgditBatch);
        form["mailingAddress"] = new { details = "House 1", divisionId = refs.Division, districtId = refs.District, upazilaId = refs.Upazila };

        var (status, _) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(0, (await CountsAsync()).Profiles);
    }

    [Fact]
    public async Task A_locality_deactivated_later_may_stay_but_cannot_be_newly_chosen_and_a_refused_update_changes_nothing()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("keep"), refs.Pgdit, refs.PgditBatch);
        form["mailingAddress"] = new { details = "House 1", districtId = refs.District, cityCorporationId = refs.CityCorporation };
        var admission = await AdmitAsync(admin, form);
        await Host.WithDbAsync(async db =>
        {
            (await db.CityCorporations.SingleAsync(c => c.Id == refs.CityCorporation)).IsActive = false;
            await db.SaveChangesAsync();
        });

        var kept = FormOf((await GetAdmission(admin, admission.Id)).Body);
        kept["applicantName"] = "Ayesha Rahman Khan";
        Assert.Equal(HttpStatusCode.OK, (await PutAdmission(admin, admission.Id, kept)).Status);

        // A new inactive choice is refused, and the name change sent with it is not saved either.
        var refused = FormOf((await GetAdmission(admin, admission.Id)).Body);
        refused["applicantName"] = "Should Not Save";
        refused["permanentAddress"] = new { details = "Village 2", districtId = refs.District, cityCorporationId = refs.InactiveCityCorporation };
        var (status, body) = await PutAdmission(admin, admission.Id, refused);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(body, "permanentAddress.cityCorporationId"));
        var profile = (await GetAdmission(admin, admission.Id)).Body.GetProperty("profile");
        Assert.Equal("Ayesha Rahman Khan", profile.GetProperty("applicantName").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("permanentAddress").ValueKind);
        Assert.Equal(refs.CityCorporation, profile.GetProperty("mailingAddress").GetProperty("cityCorporationId").GetGuid());
    }

    [Fact]
    public async Task No_attachment_signature_or_password_field_is_accepted()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();

        foreach (var field in new[] { "photo", "attachments", "signature", "password" })
        {
            var (status, _) = await CreateAdmission(admin, WithValue(Form(Unique("extra"), refs.Pgdit, refs.PgditBatch), field, "data"));
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }

        Assert.Equal(0, (await CountsAsync()).Profiles);
    }

    [Fact]
    public async Task The_directory_searches_and_filters_admissions_in_bounded_pages()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("listed");
        var first = await AdmitAsync(admin, Form(email, refs.Pgdit, refs.PgditBatch, name: "Zara Karim"));
        await AdmitAsync(admin, Form(email, refs.Emit, refs.EmitBatch, name: "Zara Karim"));
        await AdmitAsync(admin, Form(Unique("other"), refs.Emit, refs.EmitBatch, name: "Bashir Uddin"));

        var (_, byName) = await SendAsync(admin, HttpMethod.Get, $"{Students}?search=zara");
        var (_, byEmail) = await SendAsync(admin, HttpMethod.Get, $"{Students}?search={Uri.EscapeDataString(email.ToUpperInvariant())}");
        var (_, byDegree) = await SendAsync(admin, HttpMethod.Get, $"{Students}?degreeId={refs.Emit}");
        var (_, byBatch) = await SendAsync(admin, HttpMethod.Get, $"{Students}?batchId={refs.PgditBatch}");
        var (tooBig, _) = await SendAsync(admin, HttpMethod.Get, $"{Students}?pageSize=101");
        var (_, small) = await SendAsync(admin, HttpMethod.Get, $"{Students}?pageSize=1");

        Assert.Equal(2, Items(byName).Count);
        Assert.Equal(2, Items(byEmail).Count);
        Assert.Equal(2, Items(byDegree).Count);
        Assert.Equal(first.Id, Assert.Single(Items(byBatch)).GetProperty("id").GetGuid());
        Assert.Equal(["Bashir Uddin", "Zara Karim", "Zara Karim"], Items(byDegree).Concat(Items(byBatch)).Select(i => i.GetProperty("applicantName").GetString()).Order());
        Assert.Equal(HttpStatusCode.BadRequest, tooBig);
        Assert.Single(Items(small));
        Assert.Equal(3, small.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task The_directory_shows_the_class_roll_and_finds_an_admission_by_it_within_a_batch()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("roll"), refs.Emit, refs.EmitBatch, name: "Nadia Islam");
        form["classRoll"] = "EM-07-42";
        var admitted = await AdmitAsync(admin, form);
        await AdmitAsync(admin, Form(Unique("noroll"), refs.Emit, refs.EmitSecondBatch, name: "Nadia Haque"));

        var (_, byRoll) = await SendAsync(admin, HttpMethod.Get, $"{Students}?search=em-07-42");
        var (_, inBatch) = await SendAsync(admin, HttpMethod.Get, $"{Students}?search=nadia&batchId={refs.EmitBatch}");

        var item = Assert.Single(Items(byRoll));
        Assert.Equal(admitted.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("EM-07-42", item.GetProperty("classRoll").GetString());
        Assert.Equal(admitted.Id, Assert.Single(Items(inBatch)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_lookup_tells_the_form_whether_an_email_is_new_a_returning_Student_or_another_kind_of_account()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("lookup");
        await AdmitAsync(admin, Form(email, refs.Pgdit, refs.PgditBatch));

        var (_, fresh) = await SendAsync(admin, HttpMethod.Get, $"{Students}/lookup?email={Uri.EscapeDataString(Unique("new"))}");
        var (_, returning) = await SendAsync(admin, HttpMethod.Get, $"{Students}/lookup?email={Uri.EscapeDataString(email)}");
        var (_, staff) = await SendAsync(admin, HttpMethod.Get, $"{Students}/lookup?email={Uri.EscapeDataString((await UserAsync(AdminUserId)).Email!)}");
        var (invalid, _) = await SendAsync(admin, HttpMethod.Get, $"{Students}/lookup?email=nope");

        Assert.Equal("New", fresh.GetProperty("status").GetString());
        Assert.Equal("ExistingStudent", returning.GetProperty("status").GetString());
        Assert.Equal("Ayesha Rahman", returning.GetProperty("profile").GetProperty("applicantName").GetString());
        Assert.Single(returning.GetProperty("profile").GetProperty("admissions").EnumerateArray());
        Assert.Equal("NotStudentAccount", staff.GetProperty("status").GetString());
        Assert.False(staff.TryGetProperty("profile", out _));
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
    }

    [Fact]
    public async Task Anonymous_and_non_Admin_callers_see_and_change_nothing()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var admission = await AdmitAsync(admin, Form(Unique("private"), refs.Pgdit, refs.PgditBatch));
        using var anonymous = Host.CreatePortalClient();
        using var student = await SignedInAsync(PortalRoles.Student);
        using var teacher = await SignedInAsync(PortalRoles.Teacher);
        using var coordinator = await SignedInAsync(PortalRoles.Teacher, PortalRoles.Coordinator);
        var before = await CountsAsync();

        foreach (var (client, expected) in new[]
        {
            (anonymous, HttpStatusCode.Unauthorized), (student, HttpStatusCode.Forbidden), (teacher, HttpStatusCode.Forbidden), (coordinator, HttpStatusCode.Forbidden),
        })
        {
            foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
            {
                (HttpMethod.Get, Students, null),
                (HttpMethod.Get, $"{Students}/{admission.Id}", null),
                (HttpMethod.Get, $"{Students}/{admission.Id}/graduation-eligibility", null),
                (HttpMethod.Get, $"{Students}/lookup?email={Uri.EscapeDataString(admission.Email)}", null),
                (HttpMethod.Post, Students, Form(Unique("x"), refs.Emit, refs.EmitBatch)),
                (HttpMethod.Put, $"{Students}/{admission.Id}", UpdateForm(Form(null, refs.Emit, refs.EmitBatch, name: "Hacked"))),
            })
            {
                using var response = await client.SendAsync(method, url, body);
                Assert.Equal(expected, response.StatusCode);
                var text = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("Ayesha", text, StringComparison.Ordinal);
                Assert.DoesNotContain("+8801711000000", text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(before, await CountsAsync());
        var (_, unchanged) = await GetAdmission(admin, admission.Id);
        Assert.Equal("Ayesha Rahman", unchanged.GetProperty("profile").GetProperty("applicantName").GetString());
    }

    [Fact]
    public async Task An_unknown_admission_is_a_404()
    {
        await RefsAsync();
        using var admin = await AdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await GetAdmission(admin, Guid.NewGuid())).Status);
    }

    [Theory]
    [InlineData("degrees")]
    [InlineData("batches")]
    [InlineData("halls")]
    [InlineData("upazilas")]
    [InlineData("city-corporations")]
    public async Task A_configuration_record_used_by_a_student_cannot_be_deleted(string route)
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("uses"), refs.Pgdit, refs.PgditBatch);
        form["assignedHallId"] = refs.Hall;
        form["mailingAddress"] = new { details = "House 3", districtId = refs.District, upazilaId = refs.Upazila };
        form["permanentAddress"] = new { details = "House 4", districtId = refs.District, cityCorporationId = refs.CityCorporation };
        await AdmitAsync(admin, form);
        var id = route switch
        {
            "degrees" => refs.Pgdit, "batches" => refs.PgditBatch, "halls" => refs.Hall, "upazilas" => refs.Upazila, _ => refs.CityCorporation,
        };

        if (route == "degrees")
        {
            // A Degree with a Batch is already protected; take the Batch away so only the admission protects it.
            await Host.WithDbAsync(async db =>
            {
                var batch = await db.Batches.SingleAsync(b => b.Id == refs.PgditBatch);
                batch.MarkDeleted(DateTime.UtcNow, null);
                await db.SaveChangesAsync();
            });
        }

        var (status, problem) = await SendAsync(admin, HttpMethod.Delete,
            route is "upazilas" or "city-corporations" ? $"/api/admin/configuration/geography/{route}/{id}" : $"/api/admin/configuration/{route}/{id}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Deactivate it instead", problem.GetProperty("detail").GetString());
    }

    private async Task<(int Profiles, int Admissions)> ProfilesAndAdmissionsOfAsync(string userId)
    {
        var counts = (0, 0);
        await Host.WithDbAsync(async db =>
        {
            var profileIds = await db.StudentProfiles.Where(p => p.UserId == userId).Select(p => p.Id).ToListAsync();
            counts = (profileIds.Count, await db.StudentAdmissions.CountAsync(a => profileIds.Contains(a.StudentProfileId)));
        });
        return counts;
    }

    private static Dictionary<string, object?> WithValue(Dictionary<string, object?> form, params object?[] pairs)
    {
        for (var i = 0; i < pairs.Length; i += 2)
        {
            form[(string)pairs[i]!] = pairs[i + 1];
        }

        return form;
    }

    private static Dictionary<string, object?> UpdateFormWithEmail(Dictionary<string, object?> form, string email)
    {
        var copy = new Dictionary<string, object?>(form) { ["email"] = email };
        copy["admissionSerialNumber"] = "SER-2";
        copy["admissionExamRoll"] = "ROLL-2";
        return copy;
    }
}
