using System.Net;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Students;

/// <summary>
/// US2: Hall and registration number start unset and are assigned or corrected later, each on its own. A registration number is unique
/// across every admission; a refused change writes nothing; every change is audited; only an active Admin may make it.
/// </summary>
public sealed class StudentIdentifierTests : StudentTestBase
{
    private async Task<(Guid Id, Dictionary<string, object?> Current)> AdmittedAsync(Infrastructure.PortalClient admin, string? email = null)
    {
        var refs = await RefsAsync();
        var admission = await AdmitAsync(admin, Form(email ?? Unique("ids"), refs.Pgdit, refs.PgditBatch));
        var (_, body) = await GetAdmission(admin, admission.Id);
        return (admission.Id, FormOf(body));
    }

    [Fact]
    public async Task Hall_and_registration_number_can_be_assigned_one_at_a_time_and_each_change_is_audited()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var (id, form) = await AdmittedAsync(admin);
        Assert.Null((await AdmissionRowAsync(id)).AssignedHallId);
        Assert.Null((await AdmissionRowAsync(id)).RegistrationNumber);

        form["assignedHallId"] = refs.Hall;
        var (hallStatus, afterHall) = await PutAdmission(admin, id, form);
        form = FormOf(afterHall);
        form["registrationNumber"] = " 2026-ab-17 ";
        var (regStatus, afterReg) = await PutAdmission(admin, id, form);

        Assert.Equal(HttpStatusCode.OK, hallStatus);
        Assert.Equal("Jagannath Hall", afterHall.GetProperty("assignedHallName").GetString());
        Assert.False(afterHall.TryGetProperty("registrationNumber", out _));
        Assert.Equal(HttpStatusCode.OK, regStatus);
        Assert.Equal(refs.Hall, afterReg.GetProperty("assignedHallId").GetGuid());
        Assert.Equal("2026-ab-17", afterReg.GetProperty("registrationNumber").GetString());

        var updates = await EventsAsync("student.admission.updated");
        Assert.Equal(2, updates.Count);
        Assert.Contains("assignedHallId", updates[0].ChangesJson);
        Assert.DoesNotContain("registrationNumber", updates[0].ChangesJson);
        Assert.Contains("2026-ab-17", updates[1].ChangesJson);
        Assert.All(updates, e => Assert.Equal(AdminUserId, e.ActorUserId));
    }

    [Fact]
    public async Task An_unrelated_change_keeps_both_identifiers_and_either_can_be_cleared_alone()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var (id, form) = await AdmittedAsync(admin);
        form["assignedHallId"] = refs.Hall;
        form["registrationNumber"] = "REG-1";
        var (_, both) = await PutAdmission(admin, id, form);

        form = FormOf(both);
        form["applicantMobile"] = "01999999999";
        var (_, unrelated) = await PutAdmission(admin, id, form);
        form = FormOf(unrelated);
        form["registrationNumber"] = null;
        var (_, cleared) = await PutAdmission(admin, id, form);

        Assert.Equal("REG-1", unrelated.GetProperty("registrationNumber").GetString());
        Assert.Equal(refs.Hall, unrelated.GetProperty("assignedHallId").GetGuid());
        Assert.Equal(refs.Hall, cleared.GetProperty("assignedHallId").GetGuid());
        Assert.False(cleared.TryGetProperty("registrationNumber", out _));
    }

    [Fact]
    public async Task A_new_hall_must_exist_and_be_active_but_an_already_assigned_hall_may_stay_after_it_is_deactivated()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var (id, form) = await AdmittedAsync(admin);

        form["assignedHallId"] = refs.InactiveHall;
        var (inactive, inactiveProblem) = await PutAdmission(admin, id, form);
        form["assignedHallId"] = Guid.NewGuid();
        var (missing, _) = await PutAdmission(admin, id, form);
        form["assignedHallId"] = refs.OtherHall;
        Assert.Equal(HttpStatusCode.OK, (await PutAdmission(admin, id, form)).Status);
        await Host.WithDbAsync(async db =>
        {
            (await db.Halls.SingleAsync(h => h.Id == refs.OtherHall)).IsActive = false;
            await db.SaveChangesAsync();
        });
        form["classRoll"] = "42";
        var (kept, keptBody) = await PutAdmission(admin, id, form);

        Assert.Equal(HttpStatusCode.BadRequest, inactive);
        Assert.True(HasError(inactiveProblem, "assignedHallId"));
        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.Equal(HttpStatusCode.OK, kept);
        Assert.Equal(refs.OtherHall, keptBody.GetProperty("assignedHallId").GetGuid());
    }

    [Fact]
    public async Task A_registration_number_used_by_another_admission_is_a_409_and_neither_record_changes()
    {
        await RefsAsync();
        using var admin = await AdminAsync();
        var (first, firstForm) = await AdmittedAsync(admin);
        firstForm["registrationNumber"] = "REG-2026-001";
        Assert.Equal(HttpStatusCode.OK, (await PutAdmission(admin, first, firstForm)).Status);
        var (second, secondForm) = await AdmittedAsync(admin);
        secondForm["registrationNumber"] = " reg-2026-001 ";
        secondForm["classRoll"] = "99";
        var updates = (await EventsAsync("student.admission.updated")).Count;

        var (status, problem) = await PutAdmission(admin, second, secondForm);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("registration number", problem.GetProperty("detail").GetString());
        Assert.Equal("REG-2026-001", (await AdmissionRowAsync(first)).RegistrationNumber);
        Assert.Null((await AdmissionRowAsync(second)).RegistrationNumber);
        Assert.Null((await AdmissionRowAsync(second)).ClassRoll);
        Assert.Equal(updates, (await EventsAsync("student.admission.updated")).Count);
    }

    [Fact]
    public async Task A_registration_number_held_by_a_deactivated_students_admission_is_still_taken()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("gone"), refs.Pgdit, refs.PgditBatch);
        form["registrationNumber"] = "REG-OLD";
        var gone = await AdmitAsync(admin, form);
        await Host.SetAccountStatusAsync(gone.Email, TeacherAccountStatus.Deactivated);
        var second = Form(Unique("new"), refs.Emit, refs.EmitBatch);
        second["registrationNumber"] = "reg-old";

        var (status, _) = await CreateAdmission(admin, second);

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task Correcting_degree_and_batch_keeps_the_identifiers_and_rejects_a_mismatched_or_duplicate_pair()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("move");
        var (id, form) = await AdmittedAsync(admin, email);
        await AdmitAsync(admin, Form(email, refs.Emit, refs.EmitSecondBatch));
        form["assignedHallId"] = refs.Hall;
        form["registrationNumber"] = "REG-MOVE";
        var (_, assigned) = await PutAdmission(admin, id, form);

        form = FormOf(assigned);
        form["degreeId"] = refs.Emit;
        var (mismatch, _) = await PutAdmission(admin, id, form);
        form["batchId"] = refs.EmitSecondBatch;
        var (duplicate, _) = await PutAdmission(admin, id, form);
        form["batchId"] = refs.EmitBatch;
        var (moved, movedBody) = await PutAdmission(admin, id, form);

        Assert.Equal(HttpStatusCode.BadRequest, mismatch);
        Assert.Equal(HttpStatusCode.Conflict, duplicate);
        Assert.Equal(HttpStatusCode.OK, moved);
        Assert.Equal("EMIT Batch 7", movedBody.GetProperty("batchName").GetString());
        Assert.Equal("REG-MOVE", movedBody.GetProperty("registrationNumber").GetString());
        Assert.Equal(refs.Hall, movedBody.GetProperty("assignedHallId").GetGuid());
    }

    [Fact]
    public async Task Only_an_active_Admin_can_change_identifiers()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var (id, form) = await AdmittedAsync(admin);
        form["assignedHallId"] = refs.Hall;
        form["registrationNumber"] = "REG-X";
        using var anonymous = Host.CreatePortalClient();
        using var student = await SignedInAsync(PortalRoles.Student);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAdmission(anonymous, id, form)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAdmission(student, id, form)).Status);
        using var withoutToken = await admin.SendAsync(HttpMethod.Put, $"{Students}/{id}", form, withAntiforgery: false);
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        var row = await AdmissionRowAsync(id);
        Assert.Null(row.AssignedHallId);
        Assert.Null(row.RegistrationNumber);
    }

    [Fact]
    public async Task Updating_an_unknown_admission_is_a_404()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();

        var (status, _) = await PutAdmission(admin, Guid.NewGuid(), UpdateForm(Form(null, refs.Pgdit, refs.PgditBatch)));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }
}
