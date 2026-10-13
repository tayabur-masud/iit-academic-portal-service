using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>A real API host with an active-Admin client and small helpers for the configuration resources.</summary>
public abstract class ConfigurationTestBase : IDisposable
{
    protected const string Root = "/api/admin/configuration";

    protected PortalApiFactory Factory { get; } = new();

    public void Dispose() => Factory.Dispose();

    /// <summary>The identifier of the account behind the most recent <see cref="AdminAsync"/> client.</summary>
    protected string AdminUserId { get; private set; } = string.Empty;

    protected async Task<PortalClient> AdminAsync()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        AdminUserId = await Factory.CreateUserAsync(email, PortalRoles.Admin);
        var client = Factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return client;
    }

    protected async Task<PortalClient> SignedInAsync(params string[] roles)
    {
        var email = PortalApiFactory.UniqueEmail("user");
        await Factory.CreateUserAsync(email, roles);
        var client = Factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return client;
    }

    protected static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(
        PortalClient client, HttpMethod method, string url, object? body = null)
    {
        using var response = await client.SendAsync(method, url, body);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    protected static Task<(HttpStatusCode Status, JsonElement Body)> Post(PortalClient client, string route, object body) =>
        SendAsync(client, HttpMethod.Post, $"{Root}/{route}", body);

    protected static Task<(HttpStatusCode Status, JsonElement Body)> Put(PortalClient client, string route, Guid id, object body) =>
        SendAsync(client, HttpMethod.Put, $"{Root}/{route}/{id}", body);

    protected static Task<(HttpStatusCode Status, JsonElement Body)> Delete(PortalClient client, string route, Guid id) =>
        SendAsync(client, HttpMethod.Delete, $"{Root}/{route}/{id}");

    protected static Task<(HttpStatusCode Status, JsonElement Body)> Get(PortalClient client, string route, string query = "") =>
        SendAsync(client, HttpMethod.Get, $"{Root}/{route}{query}");

    /// <summary>Creates a record and returns its identifier, failing the test if it was not created.</summary>
    protected static async Task<Guid> CreateAsync(PortalClient client, string route, object body)
    {
        var (status, json) = await Post(client, route, body);
        Assert.True(status == HttpStatusCode.Created, $"Creating in {route} returned {status}: {json}");
        return json.GetProperty("id").GetGuid();
    }

    protected static List<string> Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();

    protected static bool HasError(JsonElement problem, string field) =>
        problem.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out _);

    protected static string FirstError(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field)[0].GetString()!;
}
