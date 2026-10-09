using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>Sets the connection's remote address before the application pipeline runs, as a real server would.</summary>
public sealed class FixedRemoteAddressStartupFilter(IPAddress address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            context.Connection.RemoteIpAddress = address;
            return continuation();
        });
        next(app);
    };
}
