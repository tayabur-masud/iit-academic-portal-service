using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Configuration;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.UserManagement;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Staff;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Staff;

/// <summary>Shared request shapes for the staff tests.</summary>
internal static class StaffRequests
{
    public const string Staff = "/api/admin/staff";

    public static object Create(string? email, Guid designation, string name = "Farhana Akter", string mobile = "01711000000", bool? confirmed = true) =>
        new { email, name, mobileNumber = mobile, designationId = designation, manualVerificationConfirmed = confirmed };

    public static object Update(Guid designation, string name = "Farhana Akter", string mobile = "01711000000", bool? confirmed = true) =>
        new { name, mobileNumber = mobile, designationId = designation, manualVerificationConfirmed = confirmed };
}

/// <summary>
/// Staff Management: one profile (Name, Mobile number, Designation) per Admin account, created by email (a new Admin account, or an
/// existing eligible one), maintained only by an active Admin who confirms manual verification on every save, and deactivated together
/// with its account. Every successful change is audited in the same transaction; a refused one changes neither profile nor audit.
/// New-account provisioning and its setup email are covered by <see cref="StaffProvisioningTests"/>.
/// </summary>
public sealed class StaffManagementTests : ConfigurationTestBase
{
    private const string Staff = StaffRequests.Staff;

    private sealed record Account(string UserId, string Email);

    /// <summary>Adds a Designation directly, so the signed-in Admin of the test (and its <see cref="ConfigurationTestBase.AdminUserId"/>) stays the same.</summary>
    private async Task<Guid> DesignationAsync(string name = "Lecturer", bool active = true)
    {
        var designation = new Domain.Configuration.Designation();
        designation.Rename($"{name} {Guid.NewGuid():N}");
        designation.IsActive = active;
        await Factory.WithDbAsync(async db =>
        {
            db.Designations.Add(designation);
            await db.SaveChangesAsync();
        });
        return designation.Id;
    }

    /// <summary>An existing, active Admin account with a password (so it can sign in), made outside Staff Management.</summary>
    private async Task<Account> AdminAccountAsync(string prefix = "staff")
    {
        var email = PortalApiFactory.UniqueEmail(prefix);
        return new Account(await Factory.CreateUserAsync(email, PortalRoles.Admin), email);
    }

    private static Task<(HttpStatusCode Status, JsonElement Body)> Create(PortalClient admin, object body) =>
        SendAsync(admin, HttpMethod.Post, Staff, body);

    private static Task<(HttpStatusCode Status, JsonElement Body)> Update(PortalClient admin, Guid id, object body) =>
        SendAsync(admin, HttpMethod.Put, $"{Staff}/{id}", body);

    private static Task<(HttpStatusCode Status, JsonElement Body)> Deactivate(PortalClient admin, Guid id) =>
        SendAsync(admin, HttpMethod.Post, $"{Staff}/{id}/deactivation");

    private async Task<Guid> CreatedAsync(PortalClient admin, string email, Guid designation, string name = "Farhana Akter")
    {
        var (status, body) = await Create(admin, StaffRequests.Create(email, designation, name));
        Assert.True(status == HttpStatusCode.Created, $"Creating a staff profile returned {status}: {body}");
        return body.GetProperty("id").GetGuid();
    }

    private async Task<int> ProfileCountAsync()
    {
        var count = 0;
        await Factory.WithDbAsync(async db => count = await db.StaffProfiles.CountAsync());
        return count;
    }

    private async Task<List<Domain.Auditing.AuditEvent>> StaffEventsAsync() =>
        (await Factory.AuditEventsAsync()).Where(e => e.EventType.StartsWith("staff.", StringComparison.Ordinal)).ToList();

    private async Task<PortalUser> UserAsync(string userId)
    {
        PortalUser? user = null;
        await Factory.WithDbAsync(async db => user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));
        return user!;
    }

    [Fact]
    public async Task An_existing_admin_account_is_linked_by_email_and_the_profile_reads_back()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync("Assistant Registrar");
        var account = await AdminAccountAsync("registrar");

        var (status, created) = await Create(admin, StaffRequests.Create($"  {account.Email.ToUpperInvariant()} ", designation, name: "  Farhana   Akter ", mobile: " 01711000000 "));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(created.GetProperty("accountCreated").GetBoolean());
        Assert.False(created.GetProperty("setupEmailRequested").GetBoolean());
        var id = created.GetProperty("id").GetGuid();
        var (read, profile) = await SendAsync(admin, HttpMethod.Get, $"{Staff}/{id}");
        Assert.Equal(HttpStatusCode.OK, read);
        Assert.Equal(account.UserId, profile.GetProperty("userId").GetString());
        Assert.Equal(account.Email, profile.GetProperty("email").GetString());
        Assert.Equal("Farhana   Akter", profile.GetProperty("name").GetString());
        Assert.Equal("01711000000", profile.GetProperty("mobileNumber").GetString());
        Assert.Equal(designation, profile.GetProperty("designationId").GetGuid());
        Assert.StartsWith("Assistant Registrar", profile.GetProperty("designationName").GetString());
        Assert.True(profile.GetProperty("isActive").GetBoolean());
        Assert.Equal(AdminEmail, profile.GetProperty("createdBy").GetString());
        Assert.False(profile.TryGetProperty("accountCreated", out _));
        foreach (var secret in new[] { "passwordHash", "securityStamp", "password", "isVerified", "manualVerificationConfirmed", "proof" })
        {
            Assert.False(profile.TryGetProperty(secret, out _));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(admin, HttpMethod.Get, $"{Staff}/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task Linking_records_one_profile_event_with_the_confirmation_and_never_changes_the_account()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var account = await AdminAccountAsync();
        var before = await UserAsync(account.UserId);
        var accountsBefore = 0;
        await Factory.WithDbAsync(async db => accountsBefore = await db.Users.CountAsync());

        var id = await CreatedAsync(admin, account.Email, designation);

        var created = Assert.Single(await StaffEventsAsync());
        Assert.Equal("staff.profile.created", created.EventType);
        Assert.Equal("StaffProfile", created.EntityType);
        Assert.Equal(id.ToString(), created.EntityId);
        Assert.Equal(AdminUserId, created.ActorUserId);
        using (var metadata = JsonDocument.Parse(created.MetadataJson!))
        {
            Assert.True(metadata.RootElement.GetProperty("manualVerificationConfirmed").GetBoolean());
            Assert.Equal(account.UserId, metadata.RootElement.GetProperty("userId").GetString());
            Assert.False(metadata.RootElement.GetProperty("accountCreated").GetBoolean());
        }

        Assert.Contains("\"mobileNumber\"", created.ChangesJson);
        Assert.DoesNotContain("01711000000", created.ChangesJson);
        Assert.Empty(await Factory.AuditEventsAsync("account.created"));
        var after = await UserAsync(account.UserId);
        Assert.Equal(
            $"{before.Email}|{before.AccountStatus}|{before.PasswordHash}|{before.SecurityStamp}|{before.DefaultRole}|{before.PhoneNumber}",
            $"{after.Email}|{after.AccountStatus}|{after.PasswordHash}|{after.SecurityStamp}|{after.DefaultRole}|{after.PhoneNumber}");
        await Factory.WithDbAsync(async db =>
        {
            Assert.Equal(accountsBefore, await db.Users.CountAsync());
            Assert.Equal(0, await db.NotificationRecords.CountAsync(r => r.RecipientUserId == account.UserId));
        });
    }

    [Fact]
    public async Task An_admin_updates_another_admins_profile_and_only_changed_fields_are_audited()
    {
        using var admin = await AdminAsync();
        var first = await DesignationAsync("Lecturer");
        var second = await DesignationAsync("Professor");
        var id = await CreatedAsync(admin, (await AdminAccountAsync()).Email, first);

        var (status, updated) = await Update(admin, id, StaffRequests.Update(second, name: "Farhana Akter Khan"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Farhana Akter Khan", updated.GetProperty("name").GetString());
        Assert.Equal(second, updated.GetProperty("designationId").GetGuid());
        var update = Assert.Single(await StaffEventsAsync(), e => e.EventType == "staff.profile.updated");
        Assert.Contains("Farhana Akter Khan", update.ChangesJson);
        Assert.DoesNotContain("\"mobileNumber\"", update.ChangesJson);
        using var metadata = JsonDocument.Parse(update.MetadataJson!);
        Assert.True(metadata.RootElement.GetProperty("manualVerificationConfirmed").GetBoolean());

        // Saving the same values again still needs the confirmation but changes nothing and records nothing.
        var before = (await StaffEventsAsync()).Count;
        Assert.Equal(HttpStatusCode.OK, (await Update(admin, id, StaffRequests.Update(second, name: "Farhana Akter Khan"))).Status);
        Assert.Equal(before, (await StaffEventsAsync()).Count);
    }

    [Fact]
    public async Task An_admin_updates_their_own_profile_through_the_same_route_and_rules()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var id = await CreatedAsync(admin, AdminEmail, designation, name: "Self Admin");

        var unconfirmed = await Update(admin, id, StaffRequests.Update(designation, name: "Self Admin Renamed", confirmed: false));
        var (status, updated) = await Update(admin, id, StaffRequests.Update(designation, name: "Self Admin Renamed"));

        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.Status);
        Assert.True(HasError(unconfirmed.Body, "manualVerificationConfirmed"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Self Admin Renamed", updated.GetProperty("name").GetString());
        var update = Assert.Single(await StaffEventsAsync(), e => e.EventType == "staff.profile.updated");
        Assert.Equal(AdminUserId, update.ActorUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task A_save_without_the_verification_confirmation_is_refused_and_changes_nothing(bool? confirmed)
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var email = PortalApiFactory.UniqueEmail("unconfirmed");

        var (status, body) = await Create(admin, StaffRequests.Create(email, designation, confirmed: confirmed));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Confirm that you manually verified this information against the institution's records.",
            FirstError(body, "manualVerificationConfirmed"));
        Assert.Equal(0, await ProfileCountAsync());
        Assert.Empty(await StaffEventsAsync());
        await Factory.WithDbAsync(async db => Assert.Equal(0, await db.Users.CountAsync(u => u.Email == email)));
    }

    public static TheoryData<string, string> InvalidFields => new()
    {
        { "blank-name", "name" },
        { "long-name", "name" },
        { "blank-mobile", "mobileNumber" },
        { "long-mobile", "mobileNumber" },
        { "no-designation", "designationId" },
        { "unknown-designation", "designationId" },
        { "inactive-designation", "designationId" },
        { "deleted-designation", "designationId" },
        { "no-email", "email" },
        { "bad-email", "email" },
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task Invalid_fields_are_refused_with_their_key_and_nothing_is_stored(string problem, string field)
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var inactive = await DesignationAsync("Old Post", active: false);
        var deleted = await DesignationAsync("Gone Post");
        await Factory.WithDbAsync(async db =>
        {
            (await db.Designations.SingleAsync(d => d.Id == deleted)).MarkDeleted(DateTime.UtcNow, null);
            await db.SaveChangesAsync();
        });
        var email = PortalApiFactory.UniqueEmail("invalid");
        var accounts = 0;
        await Factory.WithDbAsync(async db => accounts = await db.Users.CountAsync());

        object body = problem switch
        {
            "blank-name" => StaffRequests.Create(email, designation, name: "   "),
            "long-name" => StaffRequests.Create(email, designation, name: new string('a', 201)),
            "blank-mobile" => StaffRequests.Create(email, designation, mobile: " "),
            "long-mobile" => StaffRequests.Create(email, designation, mobile: new string('1', 51)),
            "no-designation" => new { email, name = "A", mobileNumber = "1", manualVerificationConfirmed = true },
            "unknown-designation" => StaffRequests.Create(email, Guid.NewGuid()),
            "inactive-designation" => StaffRequests.Create(email, inactive),
            "deleted-designation" => StaffRequests.Create(email, deleted),
            "no-email" => StaffRequests.Create(null, designation),
            _ => StaffRequests.Create("not-an-email", designation),
        };

        var (status, response) = await Create(admin, body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(response, field), $"Expected an error on {field}: {response}");
        Assert.Equal(0, await ProfileCountAsync());
        Assert.Empty(await StaffEventsAsync());
        await Factory.WithDbAsync(async db => Assert.Equal(accounts, await db.Users.CountAsync()));
    }

    [Fact]
    public async Task Deactivated_and_non_admin_accounts_and_a_second_profile_are_refused_and_pending_admins_are_linked()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var teacher = PortalApiFactory.UniqueEmail("teacher");
        await Factory.CreateUserAsync(teacher, PortalRoles.Teacher);
        var deactivated = await AdminAccountAsync("gone");
        await Factory.SetAccountStatusAsync(deactivated.Email, TeacherAccountStatus.Deactivated);
        var pending = await AdminAccountAsync("pending");
        await Factory.SetAccountStatusAsync(pending.Email, TeacherAccountStatus.PendingCredentialSetup);
        var once = await AdminAccountAsync("once");
        var accounts = 0;
        await Factory.WithDbAsync(async db => accounts = await db.Users.CountAsync());

        var notAdmin = await Create(admin, StaffRequests.Create(teacher, designation));
        var gone = await Create(admin, StaffRequests.Create(deactivated.Email, designation));
        var (pendingStatus, pendingBody) = await Create(admin, StaffRequests.Create(pending.Email, designation));
        await CreatedAsync(admin, once.Email, designation);
        var duplicate = await Create(admin, StaffRequests.Create(once.Email, designation, name: "Second Profile"));

        Assert.Equal(HttpStatusCode.Conflict, notAdmin.Status);
        Assert.Contains("Admin role", notAdmin.Body.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, gone.Status);
        Assert.Contains("deactivated", gone.Body.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Created, pendingStatus);
        Assert.False(pendingBody.GetProperty("accountCreated").GetBoolean());
        Assert.False(pendingBody.GetProperty("setupEmailRequested").GetBoolean());
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(pending.UserId)).AccountStatus);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.Status);
        Assert.Contains("already has a staff profile", duplicate.Body.GetProperty("detail").GetString());
        Assert.Equal(2, await ProfileCountAsync());
        await Factory.WithDbAsync(async db => Assert.Equal(accounts, await db.Users.CountAsync()));
    }

    [Fact]
    public async Task A_profile_whose_account_lost_the_admin_role_cannot_be_changed_but_stays_readable()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var account = await AdminAccountAsync();
        var id = await CreatedAsync(admin, account.Email, designation);
        await Factory.RemoveRoleAsync(account.Email, PortalRoles.Admin);

        var (status, problem) = await Update(admin, id, StaffRequests.Update(designation, name: "Changed"));
        var (read, profile) = await SendAsync(admin, HttpMethod.Get, $"{Staff}/{id}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Admin role", problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, read);
        Assert.Equal("Farhana Akter", profile.GetProperty("name").GetString());
        Assert.False(profile.GetProperty("hasAdminRole").GetBoolean());
    }

    [Fact]
    public async Task An_unchanged_designation_deactivated_later_may_stay_but_cannot_be_newly_chosen()
    {
        using var admin = await AdminAsync();
        var kept = await DesignationAsync("Kept");
        var other = await DesignationAsync("Other", active: false);
        var id = await CreatedAsync(admin, (await AdminAccountAsync()).Email, kept);
        await Factory.WithDbAsync(async db =>
        {
            (await db.Designations.SingleAsync(d => d.Id == kept)).IsActive = false;
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.OK, (await Update(admin, id, StaffRequests.Update(kept, name: "Renamed"))).Status);
        var (status, problem) = await Update(admin, id, StaffRequests.Update(other));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "designationId"));
    }

    [Fact]
    public async Task Requests_take_no_user_id_role_or_secret_fields()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var id = await CreatedAsync(admin, (await AdminAccountAsync()).Email, designation);

        var moved = await Update(admin, id, new { email = AdminEmail, name = "A", mobileNumber = "1", designationId = designation, manualVerificationConfirmed = true });
        var userId = await Create(admin, new { userId = AdminUserId, name = "A", mobileNumber = "1", designationId = designation, manualVerificationConfirmed = true });
        var role = await Create(admin, new { email = PortalApiFactory.UniqueEmail("role"), name = "A", mobileNumber = "1", designationId = designation, manualVerificationConfirmed = true, roles = new[] { "Teacher" } });
        var password = await Create(admin, new { email = PortalApiFactory.UniqueEmail("pw"), name = "A", mobileNumber = "1", designationId = designation, manualVerificationConfirmed = true, password = "x" });

        Assert.All(new[] { moved.Status, userId.Status, role.Status, password.Status }, s => Assert.Equal(HttpStatusCode.BadRequest, s));
        Assert.Equal(1, await ProfileCountAsync());
    }

    [Fact]
    public async Task The_directory_is_bounded_and_searches_name_email_mobile_and_designation()
    {
        using var admin = await AdminAsync();
        var lecturer = await DesignationAsync("Lecturer");
        var registrar = await DesignationAsync("Deputy Registrar");
        var zeta = await AdminAccountAsync("zeta");
        await CreatedAsync(admin, zeta.Email, lecturer, name: "Zeta Rahman");
        var (status, _) = await Create(admin, StaffRequests.Create((await AdminAccountAsync()).Email, registrar, name: "Alpha Hasan", mobile: "01999888777"));
        Assert.Equal(HttpStatusCode.Created, status);

        async Task<List<string>> Names(string query)
        {
            var (_, page) = await SendAsync(admin, HttpMethod.Get, $"{Staff}{query}");
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();
        }

        Assert.Equal(["Alpha Hasan", "Zeta Rahman"], await Names(""));
        Assert.Equal(["Zeta Rahman"], await Names("?search=zeta%20rahman"));
        Assert.Equal(["Zeta Rahman"], await Names($"?search={Uri.EscapeDataString(zeta.Email.ToUpperInvariant())}"));
        Assert.Equal(["Alpha Hasan"], await Names("?search=999888"));
        Assert.Equal(["Alpha Hasan"], await Names("?search=deputy%20registrar"));
        var (_, paged) = await SendAsync(admin, HttpMethod.Get, $"{Staff}?page=2&pageSize=1");
        Assert.Equal(2, paged.GetProperty("totalCount").GetInt64());
        Assert.Equal(["Zeta Rahman"], paged.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Get, $"{Staff}?pageSize=101")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Get, $"{Staff}?page=0")).Status);
    }

    [Fact]
    public async Task There_is_no_eligible_account_picker_endpoint_any_more()
    {
        using var admin = await AdminAsync();

        var (status, _) = await SendAsync(admin, HttpMethod.Get, $"{Staff}/eligible-accounts");

        Assert.True(status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, status.ToString());
    }

    [Fact]
    public async Task Deactivating_a_staff_member_disables_the_account_revokes_sessions_and_keeps_the_history()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var target = await AdminAccountAsync("leaving");
        using var targetClient = Factory.CreatePortalClient();
        await targetClient.SignInSuccessfullyAsync(target.Email);
        var id = await CreatedAsync(admin, target.Email, designation);

        var (status, body) = await Deactivate(admin, id);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal("Deactivated", body.GetProperty("accountStatus").GetString());
        Assert.Equal(TeacherAccountStatus.Deactivated, (await UserAsync(target.UserId)).AccountStatus);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(targetClient, HttpMethod.Get, Staff)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.SignInAsync(target.Email)).StatusCode);
        var deactivated = Assert.Single(await StaffEventsAsync(), e => e.EventType == "staff.profile.deactivated");
        Assert.Equal(id.ToString(), deactivated.EntityId);
        Assert.Contains("\"isActive\"", deactivated.ChangesJson);
        Assert.Contains("Deactivated", deactivated.ChangesJson);
        using (var metadata = JsonDocument.Parse(deactivated.MetadataJson!))
        {
            Assert.Equal(1, metadata.RootElement.GetProperty("sessionsRevoked").GetInt32());
        }

        // The profile, its roles, and its history remain; it is read-only from now on, and repeating changes nothing.
        Assert.Equal(1, await ProfileCountAsync());
        await Factory.WithDbAsync(async db => Assert.True(await db.UserRoles.AnyAsync(ur => ur.UserId == target.UserId)));
        Assert.Equal(HttpStatusCode.Conflict, (await Update(admin, id, StaffRequests.Update(designation, name: "Changed"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Deactivate(admin, id)).Status);
        Assert.Single(await StaffEventsAsync(), e => e.EventType == "staff.profile.deactivated");
        var (_, listed) = await SendAsync(admin, HttpMethod.Get, Staff);
        Assert.False(listed.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task An_admin_cannot_deactivate_their_own_staff_account()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var id = await CreatedAsync(admin, AdminEmail, designation);

        var (status, problem) = await Deactivate(admin, id);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("your own", problem.GetProperty("detail").GetString());
        Assert.Equal(TeacherAccountStatus.Active, (await UserAsync(AdminUserId)).AccountStatus);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(admin, HttpMethod.Get, Staff)).Status);
        Assert.DoesNotContain(await StaffEventsAsync(), e => e.EventType == "staff.profile.deactivated");
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_deactivated()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var target = await AdminAccountAsync("last");
        var id = await CreatedAsync(admin, target.Email, designation);
        // Leave the target as the only active Admin, then deactivate it outside a request (no acting Admin of its own).
        await Factory.WithDbAsync(async db =>
        {
            var adminRole = await db.Roles.SingleAsync(r => r.Name == PortalRoles.Admin);
            var others = await db.Users.Where(u => u.Id != target.UserId && db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRole.Id)).ToListAsync();
            others.ForEach(u => u.AccountStatus = TeacherAccountStatus.Deactivated);
            await db.SaveChangesAsync();
        });

        StaffResult<StaffDetail> result;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<StaffLifecycleService>().DeactivateAsync(id, CancellationToken.None);
        }

        Assert.Equal(StaffOutcome.Conflict, result.Outcome);
        Assert.Contains("last active Admin", result.Message);
        Assert.Equal(TeacherAccountStatus.Active, (await UserAsync(target.UserId)).AccountStatus);
        await Factory.WithDbAsync(async db => Assert.True((await db.StaffProfiles.SingleAsync(p => p.Id == id)).IsActive));
    }

    [Fact]
    public async Task A_designation_used_by_a_staff_profile_cannot_be_deleted()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        await CreatedAsync(admin, (await AdminAccountAsync()).Email, designation);

        var (status, problem) = await SendAsync(admin, HttpMethod.Delete, $"{Root}/designations/{designation}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Deactivate it instead", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Only_an_active_admin_session_with_the_anti_forgery_token_can_use_staff_management()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var id = await CreatedAsync(admin, (await AdminAccountAsync()).Email, designation);
        var target = await AdminAccountAsync();
        using var teacher = await SignedInAsync(PortalRoles.Teacher);
        using var anonymous = Factory.CreatePortalClient();

        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, Staff, null),
            (HttpMethod.Get, $"{Staff}/{id}", null),
            (HttpMethod.Post, Staff, StaffRequests.Create(target.Email, designation)),
            (HttpMethod.Put, $"{Staff}/{id}", StaffRequests.Update(designation, name: "Hacked")),
            (HttpMethod.Post, $"{Staff}/{id}/deactivation", null),
        })
        {
            var (teacherStatus, teacherBody) = await SendAsync(teacher, method, url, body);
            var (anonymousStatus, _) = await SendAsync(anonymous, method, url, body);
            Assert.Equal(HttpStatusCode.Forbidden, teacherStatus);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus);
            Assert.DoesNotContain("Farhana", teacherBody.ValueKind == JsonValueKind.Undefined ? "" : teacherBody.ToString());
        }

        using var noToken = await admin.SendAsync(HttpMethod.Post, Staff, StaffRequests.Create(target.Email, designation), withAntiforgery: false);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var noTokenDeactivation = await admin.SendAsync(HttpMethod.Post, $"{Staff}/{id}/deactivation", withAntiforgery: false);
        Assert.Equal(HttpStatusCode.BadRequest, noTokenDeactivation.StatusCode);
        Assert.Equal(1, await ProfileCountAsync());
        var (_, profile) = await SendAsync(admin, HttpMethod.Get, $"{Staff}/{id}");
        Assert.Equal("Farhana Akter", profile.GetProperty("name").GetString());
        Assert.True(profile.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task There_is_no_delete_reactivation_role_or_my_profile_write_route()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var id = await CreatedAsync(admin, AdminEmail, designation);

        foreach (var (method, url) in new[]
        {
            (HttpMethod.Delete, $"{Staff}/{id}"),
            (HttpMethod.Post, $"{Staff}/{id}/reactivation"),
            (HttpMethod.Put, $"{Staff}/{id}/roles"),
            (HttpMethod.Put, $"{Staff}/me"),
            (HttpMethod.Put, "/api/me/profile"),
        })
        {
            var (status, _) = await SendAsync(admin, method, url, StaffRequests.Update(designation, name: "Not Allowed"));
            Assert.True(status is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, $"{method} {url}: {status}");
        }

        Assert.Equal(1, await ProfileCountAsync());
    }

    [Fact]
    public async Task An_audit_failure_rolls_the_profile_change_and_the_deactivation_back()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var target = await AdminAccountAsync();
        var id = await CreatedAsync(admin, target.Email, designation);
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_staff_audit BEFORE INSERT ON \"AuditEvents\" WHEN NEW.\"EventType\" LIKE 'staff.%' BEGIN SELECT RAISE(ABORT, 'forced audit failure'); END;"));

        var (updateStatus, _) = await Update(admin, id, StaffRequests.Update(designation, name: "Must Not Persist"));
        var (deactivateStatus, _) = await Deactivate(admin, id);
        var (createStatus, _) = await Create(admin, StaffRequests.Create((await AdminAccountAsync()).Email, designation));

        Assert.All(new[] { updateStatus, deactivateStatus, createStatus }, s => Assert.True(s is HttpStatusCode.InternalServerError or HttpStatusCode.BadRequest, s.ToString()));
        var (_, profile) = await SendAsync(admin, HttpMethod.Get, $"{Staff}/{id}");
        Assert.Equal("Farhana Akter", profile.GetProperty("name").GetString());
        Assert.True(profile.GetProperty("isActive").GetBoolean());
        Assert.Equal(TeacherAccountStatus.Active, (await UserAsync(target.UserId)).AccountStatus);
        Assert.Equal(1, await ProfileCountAsync());
    }
}

/// <summary>
/// Creating staff with a new email: one pending Admin account (Admin role and default, no password) and its profile, committed together
/// with their audit events, then the common credential-setup email requested after the commit. A failed request leaves the account
/// pending; a failed audit write leaves no account, profile, or email.
/// </summary>
public sealed class StaffProvisioningTests : UserManagementTestBase
{
    private async Task<Guid> DesignationAsync()
    {
        var designation = new Domain.Configuration.Designation();
        designation.Rename($"Section Officer {Guid.NewGuid():N}");
        await Host.WithDbAsync(async db =>
        {
            db.Designations.Add(designation);
            await db.SaveChangesAsync();
        });
        return designation.Id;
    }

    [Fact]
    public async Task A_new_email_creates_one_pending_admin_account_and_profile_and_requests_setup_after_commit()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var email = PortalApiFactory.UniqueEmail("new-staff");

        var (status, body) = await SendAsync(admin, HttpMethod.Post, StaffRequests.Staff, StaffRequests.Create(email, designation, name: "Nusrat Jahan"));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(body.GetProperty("accountCreated").GetBoolean());
        Assert.True(body.GetProperty("setupEmailRequested").GetBoolean());
        Assert.Equal("PendingCredentialSetup", body.GetProperty("accountStatus").GetString());
        Assert.True(body.GetProperty("hasAdminRole").GetBoolean());
        var userId = body.GetProperty("userId").GetString()!;
        var user = await UserAsync(userId);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, user.AccountStatus);
        Assert.Equal(PortalRoles.Admin, user.DefaultRole);
        Assert.Null(user.PasswordHash);
        Assert.Equal([PortalRoles.Admin], await AssignedRolesAsync(userId));
        Assert.Equal(1, await UserCountAsync(email));

        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationEventType.AccountCredentialSetup, record.EventType);
        Assert.Equal(userId, record.RecipientUserId);
        Assert.True(record.ContentWithheld);

        var accountCreated = Assert.Single(await EventsAsync("account.created"));
        Assert.Equal(userId, accountCreated.EntityId);
        var profileCreated = Assert.Single(await EventsAsync("staff.profile.created"));
        Assert.Equal(body.GetProperty("id").GetString(), profileCreated.EntityId);
        using var metadata = JsonDocument.Parse(profileCreated.MetadataJson!);
        Assert.True(metadata.RootElement.GetProperty("accountCreated").GetBoolean());
        Assert.True(metadata.RootElement.GetProperty("manualVerificationConfirmed").GetBoolean());
        Assert.DoesNotContain("proof", body.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_new_staff_member_cannot_sign_in_until_setup_and_then_signs_in_as_an_admin()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var email = PortalApiFactory.UniqueEmail("activate-staff");
        var (status, _) = await SendAsync(admin, HttpMethod.Post, StaffRequests.Staff, StaffRequests.Create(email, designation));
        Assert.Equal(HttpStatusCode.Created, status);
        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await holder.SignInAsync(email)).StatusCode);

        var link = await LatestSetupLinkAsync(email);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, link.UserId, link.Proof)).Status);

        using var signedIn = await holder.SignInAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.Created, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(holder, HttpMethod.Get, StaffRequests.Staff)).Status);
    }

    [Fact]
    public async Task A_setup_request_that_cannot_be_kept_still_creates_the_staff_member_and_leaves_the_account_pending()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var email = PortalApiFactory.UniqueEmail("setup-fails");
        Host.Fault.FailAccept = true;

        var (status, body) = await SendAsync(admin, HttpMethod.Post, StaffRequests.Staff, StaffRequests.Create(email, designation));

        Host.Fault.FailAccept = false;
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(body.GetProperty("accountCreated").GetBoolean());
        Assert.False(body.GetProperty("setupEmailRequested").GetBoolean());
        var user = await UserAsync(body.GetProperty("userId").GetString()!);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, user.AccountStatus);
        Assert.Empty(await Host.RecordsAsync());
    }

    [Fact]
    public async Task A_failed_audit_write_leaves_no_account_profile_or_setup_email()
    {
        using var admin = await AdminAsync();
        var designation = await DesignationAsync();
        var email = PortalApiFactory.UniqueEmail("rolled-back");
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var service = new StaffProfileService(
                services.GetRequiredService<IStaffStore>(),
                new ThrowingRecorder(),
                services.GetRequiredService<IAuditActorDirectory>(),
                services.GetRequiredService<UserManager<PortalUser>>(),
                services.GetRequiredService<Application.Notifications.INotificationRequestService>());

            await Assert.ThrowsAsync<AuditValidationException>(() =>
                service.CreateAsync(new StaffInput(email, "Will Not Exist", "01700000000", designation, true), CancellationToken.None));
        }

        Assert.Equal(0, await UserCountAsync(email));
        await Host.WithDbAsync(async db => Assert.Equal(0, await db.StaffProfiles.CountAsync()));
        Assert.Empty(await Host.RecordsAsync());
    }

    private sealed class ThrowingRecorder : IAuditEventRecorder
    {
        public void Stage(AuditEventRequest request) => throw new AuditValidationException("forced audit failure");

        public Task RecordSecurityAsync(AuditEventRequest request) => Task.CompletedTask;
    }
}
