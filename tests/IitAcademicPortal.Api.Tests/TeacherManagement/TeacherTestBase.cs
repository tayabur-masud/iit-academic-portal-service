using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Configuration;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>A real API host, an active-Admin client, and helpers for the Teacher Management routes.</summary>
public abstract class TeacherTestBase : ConfigurationTestBase
{
    protected const string Teachers = "/api/admin/teachers";

    protected record Teacher(Guid Id, string UserId, string Email);

    /// <summary>Creates a teacher through the API and returns its identifiers, failing the test if it was not created.</summary>
    protected async Task<Teacher> CreateTeacherAsync(
        PortalClient admin, string? name = null, string? email = null, string? staffId = null, string? phone = null, Guid? designationId = null)
    {
        email ??= PortalApiFactory.UniqueEmail("teacher");
        var (status, body) = await SendAsync(admin, HttpMethod.Post, Teachers, new
        {
            displayName = name ?? "Test Teacher",
            email,
            employeeStaffId = staffId,
            phone,
            designationId,
        });
        Assert.True(status == HttpStatusCode.Created, $"Creating a teacher returned {status}: {body}");
        return new Teacher(body.GetProperty("id").GetGuid(), body.GetProperty("userId").GetString()!, email);
    }

    protected static Task<(HttpStatusCode Status, JsonElement Body)> GetTeacher(PortalClient admin, Guid id) =>
        SendAsync(admin, HttpMethod.Get, $"{Teachers}/{id}");

    protected static Task<(HttpStatusCode Status, JsonElement Body)> PutRoles(PortalClient admin, Guid id, string[]? roles, string? defaultRole = null) =>
        SendAsync(admin, HttpMethod.Put, $"{Teachers}/{id}/roles", new { roles, defaultRole });

    protected static Task<(HttpStatusCode Status, JsonElement Body)> Deactivate(PortalClient admin, Guid id) =>
        SendAsync(admin, HttpMethod.Post, $"{Teachers}/{id}/deactivation");

    protected static string[] Roles(JsonElement teacher) =>
        teacher.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();

    /// <summary>
    /// Makes an existing teacher able to act as an Admin: gives the account a password and the Active status (the later
    /// credential-setup step), then signs in and switches to the Admin role. The Admin role itself must already be assigned.
    /// </summary>
    protected async Task<PortalClient> SignInAsAdminAsync(Teacher teacher)
    {
        await Factory.ActivateWithPasswordAsync(teacher.Email);
        var client = Factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(teacher.Email);
        Assert.Equal(HttpStatusCode.OK, (await client.SetActiveRoleAsync(PortalRoles.Admin)).StatusCode);
        return client;
    }

    protected async Task<List<AuditEvent>> EventsAsync(string eventType) => await Factory.AuditEventsAsync(eventType);

    protected async Task<int> UserCountAsync(string email)
    {
        var count = 0;
        await Factory.WithDbAsync(async db => count = await db.Users.CountAsync(u => u.Email == email));
        return count;
    }

    protected async Task<int> ProfileCountAsync()
    {
        var count = 0;
        await Factory.WithDbAsync(async db => count = await db.TeacherProfiles.CountAsync());
        return count;
    }

    protected async Task<string[]> AssignedRolesAsync(string userId)
    {
        var roles = Array.Empty<string>();
        await Factory.WithDbAsync(async db => roles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToArrayAsync());
        return PortalRoles.Normalize(roles).ToArray();
    }
}
