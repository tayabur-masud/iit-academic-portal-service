using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests;

/// <summary>
/// US4: multi-role accounts enter their default role at sign-in, then switch among assigned roles per session
/// without a union of roles.
/// </summary>
public sealed class ActiveRoleTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact]
    public async Task Multi_role_account_signs_in_directly_to_its_stored_default_role()
    {
        var email = await CreateTeacherCoordinatorAsync();
        await factory.SetDefaultRoleAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();

        var context = await PortalClient.ReadJsonAsync(await client.SignInAsync(email));

        Assert.Equal(
            [PortalRoles.Teacher, PortalRoles.Coordinator],
            context.GetProperty("availableRoles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(PortalRoles.Teacher, context.GetProperty("activeRole").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/teacher")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/coordinator")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PortalRoles.Admin)] // stored default that is not (or no longer) assigned
    public async Task Without_a_valid_stored_default_the_fixed_order_picks_the_role(string? storedDefault)
    {
        var email = await CreateTeacherCoordinatorAsync();
        await factory.SetDefaultRoleAsync(email, storedDefault);
        using var client = factory.CreatePortalClient();

        var context = await PortalClient.ReadJsonAsync(await client.SignInAsync(email));

        // Order: Admin, Coordinator, Teacher, Student.
        Assert.Equal(PortalRoles.Coordinator, context.GetProperty("activeRole").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/coordinator")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/teacher")).StatusCode);
    }

    [Fact]
    public async Task Selecting_and_switching_roles_never_combines_their_permissions()
    {
        var email = await CreateTeacherCoordinatorAsync();
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        var selected = await client.SetActiveRoleAsync(PortalRoles.Teacher);
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        Assert.Equal(PortalRoles.Teacher, (await PortalClient.ReadJsonAsync(selected)).GetProperty("activeRole").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/teacher")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/coordinator")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.SetActiveRoleAsync(PortalRoles.Coordinator)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/coordinator")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/teacher")).StatusCode);
    }

    [Fact]
    public async Task Switching_to_an_unassigned_role_is_rejected_and_keeps_the_current_role()
    {
        var email = await CreateTeacherCoordinatorAsync();
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        await client.SetActiveRoleAsync(PortalRoles.Teacher);

        var response = await client.SetActiveRoleAsync(PortalRoles.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/admin")).StatusCode);
        var context = await PortalClient.ReadJsonAsync(await client.GetCurrentSessionAsync());
        Assert.Equal(PortalRoles.Teacher, context.GetProperty("activeRole").GetString());
    }

    [Fact]
    public async Task Unsupported_role_names_are_a_validation_error()
    {
        var email = await CreateTeacherCoordinatorAsync();
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SetActiveRoleAsync("Root")).StatusCode);
    }

    [Fact]
    public async Task Active_role_is_isolated_per_session()
    {
        var email = await CreateTeacherCoordinatorAsync();
        using var first = factory.CreatePortalClient();
        using var second = factory.CreatePortalClient();
        await first.SignInSuccessfullyAsync(email);
        await second.SignInSuccessfullyAsync(email);

        await first.SetActiveRoleAsync(PortalRoles.Teacher);
        await second.SetActiveRoleAsync(PortalRoles.Coordinator);

        var firstContext = await PortalClient.ReadJsonAsync(await first.GetCurrentSessionAsync());
        Assert.Equal(PortalRoles.Teacher, firstContext.GetProperty("activeRole").GetString());
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/test-probe/modules/teacher")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/test-probe/modules/coordinator")).StatusCode);
    }

    [Fact]
    public async Task Removed_active_role_falls_back_to_the_default_among_remaining_roles()
    {
        var email = await CreateTeacherCoordinatorAsync();
        await factory.SetDefaultRoleAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        await factory.RemoveRoleAsync(email, PortalRoles.Teacher);

        var context = await PortalClient.ReadJsonAsync(await client.GetCurrentSessionAsync());
        Assert.Equal([PortalRoles.Coordinator], context.GetProperty("availableRoles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(PortalRoles.Coordinator, context.GetProperty("activeRole").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test-probe/modules/teacher")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/coordinator")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SetActiveRoleAsync(PortalRoles.Teacher)).StatusCode);
    }

    [Fact]
    public async Task Role_switch_requires_an_antiforgery_token()
    {
        var email = await CreateTeacherCoordinatorAsync();
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        var response = await client.SendAsync(
            HttpMethod.Put, "/api/auth/sessions/current/active-role", new { role = PortalRoles.Teacher }, withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string> CreateTeacherCoordinatorAsync()
    {
        var email = PortalApiFactory.UniqueEmail("teacher.coordinator");
        await factory.CreateUserAsync(email, PortalRoles.Teacher, PortalRoles.Coordinator);
        return email;
    }
}
