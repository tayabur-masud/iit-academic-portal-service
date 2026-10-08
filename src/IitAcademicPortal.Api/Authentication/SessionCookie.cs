namespace IitAcademicPortal.Api.Authentication;

/// <summary>
/// The browser credential: an HttpOnly, Secure, host-only, SameSite=Strict cookie holding the opaque
/// session handle.
/// </summary>
/// <remarks>
/// The server applies no idle or maximum-age timeout. The cookie is persistent so it survives browser
/// restarts; browsers cap cookie lifetimes (about 400 days), so the cookie is re-issued whenever the
/// client reads the current session. Only explicit logout or revocation ends the server session.
/// </remarks>
public static class SessionCookie
{
    public const string Name = "__Host-iit-session";

    private static readonly TimeSpan BrowserLifetime = TimeSpan.FromDays(400);

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, string handle, DateTimeOffset now)
    {
        var options = BaseOptions();
        options.Expires = now + BrowserLifetime;
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
