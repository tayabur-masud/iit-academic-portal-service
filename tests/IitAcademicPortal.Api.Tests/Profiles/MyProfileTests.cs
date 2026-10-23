using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.Students;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Staff;
using IitAcademicPortal.Domain.Teachers;

namespace IitAcademicPortal.Api.Tests.Profiles;

/// <summary>
/// My Profile (<c>GET /api/profile</c>), and the account name the session reports for the account menu: every supported active role sees its own account email and the one profile that role reads
/// (Admin the Staff profile, Student the Student profile, Teacher and Coordinator the Teacher profile), taken only from the session.
/// A missing profile is reported as none, never replaced by another role's; the response is an allowlist with no identifiers, account
/// state, audit facts, admissions, or credentials; and there is no write.
/// </summary>
public sealed class MyProfileTests : StudentTestBase
{
    private const string Profile = "/api/profile";

    private sealed record Me(string UserId, string Email, PortalClient Client);

    private async Task<Me> SignedInAsMeAsync(params string[] roles)
    {
        var email = PortalApiFactory.UniqueEmail("me");
        var userId = await Host.CreateUserAsync(email, roles);
        var client = Host.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return new Me(userId, email, client);
    }

    private async Task<(Guid Id, string Name)> DesignationAsync(string name)
    {
        var designation = new Designation();
        designation.Rename($"{name} {Guid.NewGuid():N}");
        await Host.WithDbAsync(async db =>
        {
            db.Designations.Add(designation);
            await db.SaveChangesAsync();
        });
        return (designation.Id, designation.Name);
    }

    private Task StaffProfileAsync(string userId, string name, string mobile, Guid designation) =>
        Host.WithDbAsync(async db =>
        {
            db.StaffProfiles.Add(StaffProfile.Create(userId, name, mobile, designation));
            await db.SaveChangesAsync();
        });

    private Task TeacherProfileAsync(string userId, string name, string? staffId, string? phone, Guid? designation) =>
        Host.WithDbAsync(async db =>
        {
            var profile = TeacherProfile.Create(userId);
            profile.Update(name, staffId, phone, designation);
            db.TeacherProfiles.Add(profile);
            await db.SaveChangesAsync();
        });

    private static Task<(HttpStatusCode Status, JsonElement Body)> MineAsync(PortalClient client, string query = "") =>
        SendAsync(client, HttpMethod.Get, Profile + query);

    private static List<string> Keys(JsonElement element) => element.EnumerateObject().Select(p => p.Name).Order().ToList();

    private static async Task<string?> SessionNameAsync(PortalClient client)
    {
        using var response = await client.GetCurrentSessionAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await PortalClient.ReadJsonAsync(response)).GetProperty("displayName").GetString();
    }

    [Fact]
    public async Task An_anonymous_request_is_refused()
    {
        using var client = Host.CreatePortalClient();

        var response = await client.GetAsync(Profile);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_session_without_a_supported_role_is_refused()
    {
        var account = await SignedInAsMeAsync();

        var (status, _) = await MineAsync(account.Client);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task An_admin_sees_their_email_and_staff_profile_and_nothing_else_and_the_response_is_not_stored()
    {
        var account = await SignedInAsMeAsync(PortalRoles.Admin);
        var (designation, designationName) = await DesignationAsync("Assistant Registrar");
        await StaffProfileAsync(account.UserId, "Farhana Akter", "01711000000", designation);

        using var response = await account.Client.SendAsync(HttpMethod.Get, Profile);
        var body = await PortalClient.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(["activeRole", "email", "profile"], Keys(body));
        Assert.Equal(account.Email, body.GetProperty("email").GetString());
        Assert.Equal(PortalRoles.Admin, body.GetProperty("activeRole").GetString());
        var profile = body.GetProperty("profile");
        Assert.Equal(["designationName", "kind", "mobileNumber", "name"], Keys(profile));
        Assert.Equal("staff", profile.GetProperty("kind").GetString());
        Assert.Equal("Farhana Akter", profile.GetProperty("name").GetString());
        Assert.Equal("01711000000", profile.GetProperty("mobileNumber").GetString());
        Assert.Equal(designationName, profile.GetProperty("designationName").GetString());
        var text = body.GetRawText();
        Assert.DoesNotContain(account.UserId, text);
        Assert.DoesNotContain(designation.ToString(), text);
    }

    [Fact]
    public async Task A_designation_renamed_in_configuration_shows_its_current_name()
    {
        var account = await SignedInAsMeAsync(PortalRoles.Admin);
        var (designation, _) = await DesignationAsync("Section Officer");
        await StaffProfileAsync(account.UserId, "Farhana Akter", "01711000000", designation);
        var renamed = $"Senior Section Officer {Guid.NewGuid():N}";
        await Host.WithDbAsync(async db =>
        {
            (await db.Designations.FindAsync(designation))!.Rename(renamed);
            await db.SaveChangesAsync();
        });

        var (_, body) = await MineAsync(account.Client);

        Assert.Equal(renamed, body.GetProperty("profile").GetProperty("designationName").GetString());
    }

    [Fact]
    public async Task A_teacher_sees_their_teacher_profile_with_optional_values_absent_when_not_set()
    {
        var account = await SignedInAsMeAsync(PortalRoles.Teacher);
        await TeacherProfileAsync(account.UserId, "Dr Rezvi Shahariar", null, null, null);

        var (status, body) = await MineAsync(account.Client);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(PortalRoles.Teacher, body.GetProperty("activeRole").GetString());
        var profile = body.GetProperty("profile");
        Assert.Equal(["designationName", "displayName", "employeeStaffId", "kind", "phone"], Keys(profile));
        Assert.Equal("teacher", profile.GetProperty("kind").GetString());
        Assert.Equal("Dr Rezvi Shahariar", profile.GetProperty("displayName").GetString());
        foreach (var optional in new[] { "employeeStaffId", "phone", "designationName" })
        {
            Assert.Equal(JsonValueKind.Null, profile.GetProperty(optional).ValueKind);
        }
    }

    [Fact]
    public async Task A_coordinator_sees_the_same_accounts_teacher_profile_and_no_coordinator_profile()
    {
        var account = await SignedInAsMeAsync(PortalRoles.Teacher, PortalRoles.Coordinator);
        var (designation, designationName) = await DesignationAsync("Associate Professor");
        await TeacherProfileAsync(account.UserId, "Dr Rezvi Shahariar", "T-042", "01556408903", designation);
        Assert.Equal(HttpStatusCode.OK, (await account.Client.SetActiveRoleAsync(PortalRoles.Coordinator)).StatusCode);

        var (status, body) = await MineAsync(account.Client);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(PortalRoles.Coordinator, body.GetProperty("activeRole").GetString());
        var profile = body.GetProperty("profile");
        Assert.Equal("teacher", profile.GetProperty("kind").GetString());
        Assert.Equal("Dr Rezvi Shahariar", profile.GetProperty("displayName").GetString());
        Assert.Equal("T-042", profile.GetProperty("employeeStaffId").GetString());
        Assert.Equal("01556408903", profile.GetProperty("phone").GetString());
        Assert.Equal(designationName, profile.GetProperty("designationName").GetString());
    }

    [Fact]
    public async Task A_multi_role_account_sees_the_profile_of_its_active_role_and_the_same_email_after_switching()
    {
        var account = await SignedInAsMeAsync(PortalRoles.Admin, PortalRoles.Teacher);
        var (designation, _) = await DesignationAsync("Lecturer");
        await StaffProfileAsync(account.UserId, "Staff Name", "01711000001", designation);
        await TeacherProfileAsync(account.UserId, "Teacher Name", null, null, designation);

        Assert.Equal(HttpStatusCode.OK, (await account.Client.SetActiveRoleAsync(PortalRoles.Admin)).StatusCode);
        var (_, asAdmin) = await MineAsync(account.Client);
        Assert.Equal(HttpStatusCode.OK, (await account.Client.SetActiveRoleAsync(PortalRoles.Teacher)).StatusCode);
        var (_, asTeacher) = await MineAsync(account.Client);

        Assert.Equal("staff", asAdmin.GetProperty("profile").GetProperty("kind").GetString());
        Assert.Equal("Staff Name", asAdmin.GetProperty("profile").GetProperty("name").GetString());
        Assert.Equal("teacher", asTeacher.GetProperty("profile").GetProperty("kind").GetString());
        Assert.Equal("Teacher Name", asTeacher.GetProperty("profile").GetProperty("displayName").GetString());
        Assert.Equal(account.Email, asAdmin.GetProperty("email").GetString());
        Assert.Equal(account.Email, asTeacher.GetProperty("email").GetString());
    }

    [Fact]
    public async Task A_missing_profile_is_reported_as_none_and_never_replaced_by_another_roles_profile()
    {
        var account = await SignedInAsMeAsync(PortalRoles.Admin, PortalRoles.Teacher);
        await TeacherProfileAsync(account.UserId, "Only A Teacher Profile", null, null, null);
        Assert.Equal(HttpStatusCode.OK, (await account.Client.SetActiveRoleAsync(PortalRoles.Admin)).StatusCode);

        var (status, body) = await MineAsync(account.Client);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(account.Email, body.GetProperty("email").GetString());
        Assert.Equal(PortalRoles.Admin, body.GetProperty("activeRole").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("profile").ValueKind);
        Assert.DoesNotContain("Only A Teacher Profile", body.GetRawText());

        var coordinator = await SignedInAsMeAsync(PortalRoles.Coordinator);
        var (_, coordinatorBody) = await MineAsync(coordinator.Client);
        Assert.Equal(JsonValueKind.Null, coordinatorBody.GetProperty("profile").ValueKind);
    }

    [Fact]
    public async Task The_account_and_role_come_only_from_the_session_and_identifiers_in_the_request_are_ignored()
    {
        var other = await SignedInAsMeAsync(PortalRoles.Admin);
        var (designation, _) = await DesignationAsync("Registrar");
        await StaffProfileAsync(other.UserId, "Other Person", "01799999999", designation);
        var me = await SignedInAsMeAsync(PortalRoles.Teacher);
        await TeacherProfileAsync(me.UserId, "Me Myself", null, null, null);

        var (status, body) = await MineAsync(me.Client, $"?userId={other.UserId}&email={Uri.EscapeDataString(other.Email)}&role=Admin");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(me.Email, body.GetProperty("email").GetString());
        Assert.Equal("Me Myself", body.GetProperty("profile").GetProperty("displayName").GetString());
        Assert.DoesNotContain("Other Person", body.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, (await me.Client.GetAsync($"{Profile}/{other.UserId}")).StatusCode);
    }

    [Fact]
    public async Task There_is_no_write_and_a_write_attempt_changes_nothing()
    {
        var account = await SignedInAsMeAsync(PortalRoles.Teacher);
        await TeacherProfileAsync(account.UserId, "Unchanged Name", null, null, null);

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Post, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var response = await account.Client.SendAsync(method, Profile, new { displayName = "Changed Name" });
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        var (_, body) = await MineAsync(account.Client);
        Assert.Equal("Unchanged Name", body.GetProperty("profile").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task A_student_sees_every_personal_detail_address_guardian_and_prior_education_but_no_identifiers_or_admissions()
    {
        using var admin = await AdminAsync();
        var refs = await RefsAsync();
        var email = Unique("student");
        var form = Form(email, refs.Pgdit, refs.PgditBatch);
        form["mailingAddress"] = new { details = "House 12, Road 5", districtId = refs.District, upazilaId = refs.Upazila, cellNumber = "01811000000" };
        form["permanentAddress"] = new { details = "Holding 7, Tongi", districtId = refs.District, cityCorporationId = refs.CityCorporation };
        form["guardianName"] = "Abdul Karim";
        form["guardianRelationship"] = "Uncle";
        form["guardianAddress"] = "Mirpur 10, Dhaka";
        form["guardianCellNumber"] = "01911000000";
        form["registrationNumber"] = "REG-" + Guid.NewGuid().ToString("N")[..6];
        form["academicHistory"] = new object[]
        {
            new { category = "SscOLevel", examination = "SSC", schoolOrCollege = "Motijheel Model High School", board = "Dhaka", group = "Science", year = "2013", cgpa = "5.00" },
            new { category = "BachelorGraduation", university = "University of Dhaka", subject = "CSE", year = "2019", cgpa = "3.70", @class = "First" },
        };
        var admission = await AdmitAsync(admin, form);
        await Host.ActivateWithPasswordAsync(email);
        using var student = Host.CreatePortalClient();
        await student.SignInSuccessfullyAsync(email);

        var (status, body) = await MineAsync(student);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal(PortalRoles.Student, body.GetProperty("activeRole").GetString());
        var profile = body.GetProperty("profile");
        Assert.Equal(
            ["academicHistory", "applicantMobile", "applicantName", "dateOfBirth", "fatherName", "guardianAddress", "guardianCellNumber",
             "guardianName", "guardianRelationship", "kind", "mailingAddress", "maritalStatus", "motherName", "nationality", "permanentAddress",
             "placeOfBirth"],
            Keys(profile));
        Assert.Equal("student", profile.GetProperty("kind").GetString());
        Assert.Equal("Ayesha Rahman", profile.GetProperty("applicantName").GetString());
        Assert.Equal("Abdur Rahman", profile.GetProperty("fatherName").GetString());
        Assert.Equal("Salma Begum", profile.GetProperty("motherName").GetString());
        Assert.Equal("1998-04-12", profile.GetProperty("dateOfBirth").GetString());
        Assert.Equal("Dhaka", profile.GetProperty("placeOfBirth").GetString());
        Assert.Equal("Single", profile.GetProperty("maritalStatus").GetString());
        Assert.Equal("Bangladeshi", profile.GetProperty("nationality").GetString());
        Assert.Equal("+8801711000000", profile.GetProperty("applicantMobile").GetString());
        Assert.Equal("Abdul Karim", profile.GetProperty("guardianName").GetString());
        Assert.Equal("Uncle", profile.GetProperty("guardianRelationship").GetString());
        Assert.Equal("Mirpur 10, Dhaka", profile.GetProperty("guardianAddress").GetString());
        Assert.Equal("01911000000", profile.GetProperty("guardianCellNumber").GetString());

        var mailing = profile.GetProperty("mailingAddress");
        Assert.Equal(["cellNumber", "cityCorporationName", "details", "districtName", "divisionName", "upazilaName"], Keys(mailing));
        Assert.Equal("House 12, Road 5", mailing.GetProperty("details").GetString());
        Assert.Equal("01811000000", mailing.GetProperty("cellNumber").GetString());
        Assert.Equal("Kaliakair", mailing.GetProperty("upazilaName").GetString());
        Assert.Equal(JsonValueKind.Null, mailing.GetProperty("cityCorporationName").ValueKind);
        Assert.Equal("Gazipur", mailing.GetProperty("districtName").GetString());
        Assert.Equal("Dhaka", mailing.GetProperty("divisionName").GetString());
        var permanent = profile.GetProperty("permanentAddress");
        Assert.Equal("Gazipur City Corporation", permanent.GetProperty("cityCorporationName").GetString());
        Assert.Equal(JsonValueKind.Null, permanent.GetProperty("upazilaName").ValueKind);

        var history = profile.GetProperty("academicHistory").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(
            ["board", "category", "cgpa", "class", "department", "division", "examination", "group", "institution", "marksObtained",
             "marksPercentage", "schoolOrCollege", "subject", "university", "year"],
            Keys(history[0]));
        Assert.Equal("SscOLevel", history[0].GetProperty("category").GetString());
        Assert.Equal("Motijheel Model High School", history[0].GetProperty("schoolOrCollege").GetString());
        Assert.Equal("5.00", history[0].GetProperty("cgpa").GetString());
        Assert.Equal("BachelorGraduation", history[1].GetProperty("category").GetString());
        Assert.Equal("University of Dhaka", history[1].GetProperty("university").GetString());
        Assert.Equal("First", history[1].GetProperty("class").GetString());

        var text = body.GetRawText();
        foreach (var hidden in new[]
                 {
                     admission.Id.ToString(), admission.ProfileId.ToString(), admission.UserId, refs.Upazila.ToString(),
                     refs.CityCorporation.ToString(), refs.District.ToString(), refs.Pgdit.ToString(), refs.PgditBatch.ToString(),
                     (string)form["registrationNumber"]!, (string)form["admissionExamRoll"]!,
                 })
        {
            Assert.DoesNotContain(hidden, text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var key in new[] { "\"id\"", "userId", "admissions", "accountStatus", "createdAtUtc", "lastModified", "password", "proof", "roles" })
        {
            Assert.DoesNotContain(key, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_student_without_optional_details_gets_them_as_absent_not_invented()
    {
        using var admin = await AdminAsync();
        var refs = await RefsAsync();
        var email = Unique("student");
        var form = Form(email, refs.Emit, refs.EmitBatch, name: "Rafiq Hasan");
        foreach (var optional in new[] { "fatherName", "motherName", "dateOfBirth", "placeOfBirth", "maritalStatus", "nationality" })
        {
            form[optional] = null;
        }

        await AdmitAsync(admin, form);
        await Host.ActivateWithPasswordAsync(email);
        using var student = Host.CreatePortalClient();
        await student.SignInSuccessfullyAsync(email);

        var (_, body) = await MineAsync(student);

        var profile = body.GetProperty("profile");
        Assert.Equal("Rafiq Hasan", profile.GetProperty("applicantName").GetString());
        foreach (var absent in new[]
                 {
                     "fatherName", "motherName", "dateOfBirth", "placeOfBirth", "maritalStatus", "nationality", "mailingAddress", "permanentAddress",
                     "guardianName", "guardianRelationship", "guardianAddress", "guardianCellNumber",
                 })
        {
            Assert.Equal(JsonValueKind.Null, profile.GetProperty(absent).ValueKind);
        }

        Assert.Empty(profile.GetProperty("academicHistory").EnumerateArray());
    }

    [Fact]
    public async Task Sign_in_the_current_session_and_a_role_switch_report_the_active_roles_profile_name()
    {
        var email = PortalApiFactory.UniqueEmail("named");
        var userId = await Host.CreateUserAsync(email, PortalRoles.Admin, PortalRoles.Teacher);
        var (designation, _) = await DesignationAsync("Lecturer");
        await StaffProfileAsync(userId, "Farhana Akter", "01711000000", designation);
        await TeacherProfileAsync(userId, "Dr Farhana Akter", null, null, designation);
        using var client = Host.CreatePortalClient();

        using var signIn = await client.SignInAsync(email);
        var signedIn = await PortalClient.ReadJsonAsync(signIn);

        Assert.Equal(HttpStatusCode.Created, signIn.StatusCode);
        Assert.Equal(["activeRole", "availableRoles", "displayName"], Keys(signedIn));
        Assert.Equal(PortalRoles.Admin, signedIn.GetProperty("activeRole").GetString());
        Assert.Equal("Farhana Akter", signedIn.GetProperty("displayName").GetString());
        Assert.Equal("Farhana Akter", await SessionNameAsync(client));

        using var switched = await client.SetActiveRoleAsync(PortalRoles.Teacher);
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        Assert.Equal("Dr Farhana Akter", (await PortalClient.ReadJsonAsync(switched)).GetProperty("displayName").GetString());
        Assert.Equal("Dr Farhana Akter", await SessionNameAsync(client));
    }

    [Fact]
    public async Task The_session_name_falls_back_to_another_profile_then_to_the_email_and_follows_profile_changes()
    {
        var withOtherProfile = await SignedInAsMeAsync(PortalRoles.Admin, PortalRoles.Teacher);
        await TeacherProfileAsync(withOtherProfile.UserId, "Only Teacher Name", null, null, null);
        Assert.Equal(HttpStatusCode.OK, (await withOtherProfile.Client.SetActiveRoleAsync(PortalRoles.Admin)).StatusCode);
        Assert.Equal("Only Teacher Name", await SessionNameAsync(withOtherProfile.Client));

        var withoutProfile = await SignedInAsMeAsync(PortalRoles.Student);
        Assert.Equal(withoutProfile.Email, await SessionNameAsync(withoutProfile.Client));

        var withoutRole = await SignedInAsMeAsync();
        Assert.Equal(withoutRole.Email, await SessionNameAsync(withoutRole.Client));

        await Host.WithDbAsync(async db =>
        {
            var profile = TeacherProfile.Create(withoutProfile.UserId);
            profile.Update("Added Later", null, null, null);
            db.TeacherProfiles.Add(profile);
            await db.SaveChangesAsync();
        });
        Assert.Equal("Added Later", await SessionNameAsync(withoutProfile.Client));
    }
}
