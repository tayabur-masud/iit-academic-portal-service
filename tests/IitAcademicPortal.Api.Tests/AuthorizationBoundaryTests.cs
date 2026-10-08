using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests;

/// <summary>US2: server-side module and record boundaries for each role, including direct requests.</summary>
public sealed class AuthorizationBoundaryTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Theory]
    [InlineData(PortalRoles.Admin, "admin")]
    [InlineData(PortalRoles.Student, "student")]
    [InlineData(PortalRoles.Teacher, "teacher")]
    [InlineData(PortalRoles.Coordinator, "coordinator")]
    public async Task Each_role_reaches_only_its_own_module(string role, string module)
    {
        var email = PortalApiFactory.UniqueEmail(module);
        await factory.CreateUserAsync(email, role);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        foreach (var other in new[] { "admin", "student", "teacher", "coordinator" })
        {
            var response = await client.GetAsync($"/test-probe/modules/{other}");
            var expected = other == module ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            Assert.Equal(expected, response.StatusCode);
            await AssertNoProtectedDataUnlessAllowed(response);
        }
    }

    [Fact]
    public async Task Student_reaches_only_their_own_records()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        var studentId = await factory.CreateUserAsync(email, PortalRoles.Student);
        var otherStudentId = await factory.CreateUserAsync(PortalApiFactory.UniqueEmail("other"), PortalRoles.Student);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/test-probe/student-records/{studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/test-probe/results/{studentId}/any-coordinator")).StatusCode);

        var denied = await client.GetAsync($"/test-probe/student-records/{otherStudentId}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await AssertNoProtectedDataUnlessAllowed(denied);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/test-probe/results/{otherStudentId}/any-coordinator")).StatusCode);
    }

    [Fact]
    public async Task Teacher_reaches_only_assigned_courses_and_components()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        var teacherId = await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/test-probe/courses/{teacherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/courses/another-teacher")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/test-probe/student-records/{teacherId}")).StatusCode);
    }

    [Fact]
    public async Task Coordinator_reaches_only_assigned_batches_and_their_results()
    {
        var email = PortalApiFactory.UniqueEmail("coordinator");
        var coordinatorId = await factory.CreateUserAsync(email, PortalRoles.Coordinator);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/test-probe/batches/{coordinatorId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/test-probe/results/some-student/{coordinatorId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/batches/another-coordinator")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/results/some-student/another-coordinator")).StatusCode);
    }

    [Fact]
    public async Task Admin_does_not_inherit_record_access()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        var adminId = await factory.CreateUserAsync(email, PortalRoles.Admin);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/test-probe/student-records/{adminId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/test-probe/courses/{adminId}")).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_direct_requests_are_rejected()
    {
        using var client = factory.CreatePortalClient();

        var response = await client.GetAsync("/test-probe/modules/admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoProtectedDataUnlessAllowed(response);
    }

    [Fact]
    public async Task Removing_a_role_assignment_stops_authorizing_it_on_the_next_request()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        await factory.CreateUserAsync(email, PortalRoles.Student);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/student")).StatusCode);

        await factory.RemoveRoleAsync(email, PortalRoles.Student);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/student")).StatusCode);
        var context = await PortalClient.ReadJsonAsync(await client.GetCurrentSessionAsync());
        Assert.Empty(context.GetProperty("availableRoles").EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, context.GetProperty("activeRole").ValueKind);
    }

    [Fact]
    public async Task Account_without_a_supported_role_signs_in_but_reaches_no_module()
    {
        var email = PortalApiFactory.UniqueEmail("unassigned");
        await factory.CreateUserAsync(email);
        using var client = factory.CreatePortalClient();

        var signIn = await client.SignInAsync(email);

        Assert.Equal(HttpStatusCode.Created, signIn.StatusCode);
        Assert.Empty((await PortalClient.ReadJsonAsync(signIn)).GetProperty("availableRoles").EnumerateArray());
        foreach (var module in new[] { "admin", "student", "teacher", "coordinator" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/test-probe/modules/{module}")).StatusCode);
        }
    }

    private static async Task AssertNoProtectedDataUnlessAllowed(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.DoesNotContain(ProbeController.ProtectedMarker, await response.Content.ReadAsStringAsync());
        }
    }
}
