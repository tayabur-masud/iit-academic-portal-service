using System.Net;
using System.Text.Json.Serialization;
using IitAcademicPortal.Api.Auditing;
using IitAcademicPortal.Api.Authentication;
using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using IitAcademicPortal.Application.Abstractions;

namespace IitAcademicPortal.Api;

public static class PortalApi
{
    public static IServiceCollection AddPortalApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProblemDetails();

        services
            .AddControllers(options => options.Filters.Add<ValidateAntiforgeryTokenFilter>())
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

        services.AddAuthentication(PortalSessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, PortalSessionAuthenticationHandler>(PortalSessionAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder().AddPortalPolicies();
        services.AddSingleton<IAuthorizationHandler, RecordAccessHandler>();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "__Host-iit-csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });

        services.AddPortalRateLimiting(configuration);
        services.AddOpenApi();

        services.AddHttpContextAccessor();
        services.AddScoped<IAuditRequestContext, AuditRequestContext>();
        services.AddAuditHealth();

        // The audit source is the client IP. Forwarded headers are honored only from the configured proxies;
        // with none configured, the source is the direct connection address and no client header is trusted.
        var knownProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        if (knownProxies.Length > 0)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                foreach (var proxy in knownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            });
        }

        return services;
    }

    public static WebApplication UsePortalApi(this WebApplication app)
    {
        // Must run first so every later component sees the real client address.
        if (app.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() is { Length: > 0 })
        {
            app.UseForwardedHeaders();
        }

        // Centralized errors: user-safe problem responses, never stack traces outside Development.
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        // Authenticated API responses must not be served from browser or proxy caches after sign-out.
        // Applied at response start so components that set their own value (antiforgery) are not overridden.
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (string.IsNullOrEmpty(context.Response.Headers.CacheControl))
                {
                    context.Response.Headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            return next(context);
        });

        // Swagger UI is static middleware, so it must run before the secure-by-default fallback policy.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "IIT Academic Portal API v1");
                options.DocumentTitle = "IIT Academic Portal API";

                // Same-origin, so the browser sends the session cookie itself. State-changing calls also need
                // an anti-forgery token, fetched fresh because it is bound to the signed-in identity.
                // Swashbuckle rebuilds this with `new Function`, which drops `async`, so return a Promise
                // instead of using await.
                options.UseRequestInterceptor("""
                    function (request) {
                      var method = (request.method || 'GET').toUpperCase();
                      if (method === 'GET' || method === 'HEAD' || method === 'OPTIONS') {
                        return request;
                      }
                      return fetch('/api/auth/anti-forgery-token', { credentials: 'same-origin' })
                        .then(function (response) { return response.json(); })
                        .then(function (token) {
                          request.headers['X-CSRF-Token'] = token.requestToken;
                          return request;
                        });
                    }
                    """);
            });
        }

        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapControllers();
        app.MapAuditHealth();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }
}
