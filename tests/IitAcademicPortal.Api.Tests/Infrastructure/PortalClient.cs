using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>Mimics the Angular client: fetches an anti-forgery token for every state-changing request.</summary>
public sealed class PortalClient(HttpClient http, CookieContainer cookies) : IDisposable
{
    public const string SessionCookieName = "__Host-iit-session";

    private static readonly Uri Origin = new("https://localhost");

    public string? SessionCookie => cookies.GetCookies(Origin)[SessionCookieName]?.Value;

    public void UseSessionCookie(string value) => cookies.Add(Origin, new Cookie(SessionCookieName, value, "/") { Secure = true });

    public Task<HttpResponseMessage> SignInAsync(string email, string password = PortalApiFactory.Password) =>
        SendAsync(HttpMethod.Post, "/api/auth/sessions", new { email, password });

    public async Task SignInSuccessfullyAsync(string email)
    {
        var response = await SignInAsync(email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => http.GetAsync(url);

    public Task<HttpResponseMessage> GetCurrentSessionAsync() => http.GetAsync("/api/auth/sessions/current");

    public Task<HttpResponseMessage> SignOutAsync() => SendAsync(HttpMethod.Delete, "/api/auth/sessions/current");

    public Task<HttpResponseMessage> SetActiveRoleAsync(string role) =>
        SendAsync(HttpMethod.Put, "/api/auth/sessions/current/active-role", new { role });

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, bool withAntiforgery = true)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (withAntiforgery)
        {
            request.Headers.Add("X-CSRF-Token", await GetAntiforgeryTokenAsync());
        }

        return await http.SendAsync(request);
    }

    public async Task<string> GetAntiforgeryTokenAsync()
    {
        var token = await http.GetFromJsonAsync<JsonElement>("/api/auth/anti-forgery-token");
        return token.GetProperty("requestToken").GetString()!;
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public void Dispose() => http.Dispose();
}
