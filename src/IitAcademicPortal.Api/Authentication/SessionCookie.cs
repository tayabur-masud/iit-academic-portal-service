using IitAcademicPortal.Domain.Sessions;

namespace IitAcademicPortal.Api.Authentication;

/// <summary>
/// The browser credential: an HttpOnly, Secure, host-only, SameSite=Strict cookie holding the opaque
/// session handle.
/// </summary>
/// <remarks>The cookie's sliding expiry matches the server's three-hour inactivity timeout.</remarks>
public static class SessionCookie
{
    public const string Name = "__Host-iit-session";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, string handle, DateTimeOffset now)
    {
        var options = BaseOptions();
        options.Expires = now + AuthSession.IdleTimeout;
        response.Cookies.Append(Name, handle, options);
    }

    public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, BaseOptions());

    private static CookieOptions BaseOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };
}
