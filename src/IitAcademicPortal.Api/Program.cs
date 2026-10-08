using IitAcademicPortal.Api;
using IitAcademicPortal.Api.Development;
using IitAcademicPortal.Application;
using IitAcademicPortal.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddPortalApi(builder.Configuration);

var app = builder.Build();

app.UsePortalApi();

if (app.Environment.IsDevelopment())
{
    await DevelopmentDataSeeder.SeedAsync(app.Services, app.Configuration, app.Logger);
}

app.Run();

public partial class Program;
