using IitAcademicPortal.Application.Authorization;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Domain.Authorization;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests.Unit;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("abcdefg1", true)]
    [InlineData("1234567a", true)]
    [InlineData("abc1", false)]
    [InlineData("abcdefgh", false)]
    [InlineData("12345678", false)]
    [InlineData("", false)]
    public void Requires_eight_characters_a_letter_and_a_number(string password, bool valid) =>
        Assert.Equal(valid, PasswordPolicy.Validate(password).Count == 0);
}

public sealed class RecordAccessRulesTests
{
    private sealed record Result(string StudentUserId, IReadOnlyCollection<string> AssignedCoordinatorUserIds)
        : IStudentOwnedRecord, ICoordinatorAssignedRecord;

    private sealed record Component(IReadOnlyCollection<string> AssignedTeacherUserIds) : ITeacherAssignedRecord;

    [Fact]
    public void Evaluates_only_the_active_role()
    {
        // The same person is the student owner and an assigned coordinator; access depends on the active role.
        var record = new Result("u1", ["u1"]);

        Assert.True(RecordAccessRules.CanAccess(PortalRoles.Student, "u1", record));
        Assert.True(RecordAccessRules.CanAccess(PortalRoles.Coordinator, "u1", record));
        Assert.False(RecordAccessRules.CanAccess(PortalRoles.Teacher, "u1", record));
        Assert.False(RecordAccessRules.CanAccess(null, "u1", record));
    }

    [Fact]
    public void Teacher_access_requires_assignment()
    {
        Assert.True(RecordAccessRules.CanAccess(PortalRoles.Teacher, "t1", new Component(["t1", "t2"])));
        Assert.False(RecordAccessRules.CanAccess(PortalRoles.Teacher, "t3", new Component(["t1", "t2"])));
    }

    [Fact]
    public void Admin_has_no_record_boundary_access() =>
        Assert.False(RecordAccessRules.CanAccess(PortalRoles.Admin, "a1", new Result("a1", ["a1"])));
}
