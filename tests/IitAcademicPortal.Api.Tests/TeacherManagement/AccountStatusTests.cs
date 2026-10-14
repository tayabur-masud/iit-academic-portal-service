using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>
/// Foundation for Teacher Management: only an Active account can sign in or keep using a session, and every active session of
/// an account can be revoked at once.
/// </summary>
public sealed class AccountStatusTests : IDisposable
{
    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static string WithoutTraceId(string problem) => System.Text.RegularExpressions.Regex.Replace(problem, "\"traceId\":\"[^\"]*\"", string.Empty);

    [Fact]
    public async Task Accounts_default_to_Active()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);

        await factory.WithDbAsync(async db =>
            Assert.Equal(TeacherAccountStatus.Active, (await db.Users.SingleAsync(u => u.Email == email)).AccountStatus));
    }

    [Theory]
    [InlineData(TeacherAccountStatus.PendingCredentialSetup)]
    [InlineData(TeacherAccountStatus.Deactivated)]
    public async Task A_non_active_account_cannot_sign_in_and_the_failure_looks_like_a_wrong_password(TeacherAccountStatus status)
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        await factory.SetAccountStatusAsync(email, status);
        using var client = factory.CreatePortalClient();
        using var wrongPasswordClient = factory.CreatePortalClient();

        var denied = await client.SignInAsync(email);
        var wrongPassword = await wrongPasswordClient.SignInAsync(PortalApiFactory.UniqueEmail("nobody"), "Wrong-pass1");

        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(WithoutTraceId(await denied.Content.ReadAsStringAsync()), WithoutTraceId(await wrongPassword.Content.ReadAsStringAsync()));
        Assert.Null(client.SessionCookie);
    }

    [Theory]
    [InlineData(TeacherAccountStatus.PendingCredentialSetup)]
    [InlineData(TeacherAccountStatus.Deactivated)]
    public async Task An_existing_session_stops_working_when_the_account_is_no_longer_active(TeacherAccountStatus status)
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetCurrentSessionAsync()).StatusCode);

        await factory.SetAccountStatusAsync(email, status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetCurrentSessionAsync()).StatusCode);
    }

    [Fact]
    public async Task Reactivating_an_account_lets_it_sign_in_again()
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        await factory.CreateUserAsync(email, PortalRoles.Teacher);
        await factory.SetAccountStatusAsync(email, TeacherAccountStatus.PendingCredentialSetup);
        using var client = factory.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(email)).StatusCode);

        await factory.SetAccountStatusAsync(email, TeacherAccountStatus.Active);

        await client.SignInSuccessfullyAsync(email);
    }

    [Fact]
    public async Task Revoking_all_sessions_of_one_account_leaves_other_accounts_alone()
    {
        var first = PortalApiFactory.UniqueEmail("teacher");
        var other = PortalApiFactory.UniqueEmail("teacher");
        var firstId = await factory.CreateUserAsync(first, PortalRoles.Teacher);
        await factory.CreateUserAsync(other, PortalRoles.Teacher);
        using var phone = factory.CreatePortalClient();
        using var laptop = factory.CreatePortalClient();
        using var bystander = factory.CreatePortalClient();
        await phone.SignInSuccessfullyAsync(first);
        await laptop.SignInSuccessfullyAsync(first);
        await bystander.SignInSuccessfullyAsync(other);

        int revoked = 0;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<IAuthSessionRepository>();
            revoked = await sessions.RevokeAllActiveForUserAsync(
                firstId, SessionRevocationReason.TeacherDeactivated, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        Assert.Equal(2, revoked);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bystander.GetCurrentSessionAsync()).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var reasons = await db.AuthSessions.Where(s => s.UserId == firstId).Select(s => s.RevocationReason).ToListAsync();
            Assert.All(reasons, r => Assert.Equal(SessionRevocationReason.TeacherDeactivated, r));
        });
    }
}
