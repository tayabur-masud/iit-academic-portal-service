using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Infrastructure;
using IitAcademicPortal.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// Provider isolation: Brevo is a Development-only adapter behind the same <see cref="IEmailSender"/> every producer uses. Production
/// refuses it at startup, a destination outside the allowlist is rejected before any HTTP request, and swapping the provider changes
/// no producer request. No Brevo credential is needed: the HTTP layer is a fake.
/// </summary>
public sealed class NotificationProviderContractTests
{
    private const string Allowed = "mailbox.one@iit.test";

    /// <summary>Records every request the adapter makes and answers with a scripted response.</summary>
    private sealed class FakeBrevo(Func<HttpRequestMessage, Task<HttpResponseMessage>>? respond = null) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri, string? ApiKey, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri, request.Headers.TryGetValues("api-key", out var key) ? key.Single() : null,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond is null
                ? new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"messageId\":\"<1@brevo>\"}") }
                : await respond(request);
        }
    }

    private static BrevoEmailSender Brevo(FakeBrevo handler, params string[] allowed) =>
        new(new HttpClient(handler), Options.Create(new BrevoOptions { ApiKey = "test-key-not-real", SenderEmail = "sender@iit.test", AllowedRecipients = allowed }));

    private static EmailMessage Message(string to = Allowed) => new(to, "Subject line", "Body text");

    private static async Task<EmailDeliveryException> FailAsync(BrevoEmailSender sender, EmailMessage message) =>
        await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(message, CancellationToken.None));

    // --- The Brevo adapter ---

    [Fact]
    public async Task One_message_is_one_request_to_the_transactional_endpoint_with_a_single_recipient_and_plain_text()
    {
        var handler = new FakeBrevo();

        await Brevo(handler, Allowed).SendAsync(Message(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", request.Uri!.ToString());
        Assert.Equal("test-key-not-real", request.ApiKey);
        var json = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("sender@iit.test", json.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal([Allowed], json.GetProperty("to").EnumerateArray().Select(t => t.GetProperty("email").GetString()));
        Assert.Equal("Subject line", json.GetProperty("subject").GetString());
        Assert.Equal("Body text", json.GetProperty("textContent").GetString());
        Assert.False(json.TryGetProperty("htmlContent", out _));
        Assert.False(json.TryGetProperty("cc", out _));
        Assert.False(json.TryGetProperty("bcc", out _));
    }

    [Fact]
    public async Task A_destination_outside_the_allowlist_is_rejected_before_any_http_request()
    {
        var handler = new FakeBrevo();

        var error = await FailAsync(Brevo(handler, Allowed), Message("someone.else@iit.test"));

        Assert.Equal(EmailFailureKind.Permanent, error.Kind);
        Assert.Equal("RecipientNotAllowed", error.Code);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain("someone.else", error.Message + error.Code);
    }

    [Fact]
    public async Task An_empty_allowlist_rejects_every_send()
    {
        var handler = new FakeBrevo();

        var error = await FailAsync(Brevo(handler), Message(Allowed));

        Assert.Equal("RecipientNotAllowed", error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_allowlist_matches_exact_addresses_ignoring_case_and_never_by_domain_or_prefix()
    {
        var handler = new FakeBrevo();
        var sender = Brevo(handler, Allowed);

        await sender.SendAsync(Message(Allowed.ToUpperInvariant()), CancellationToken.None);
        var domain = await FailAsync(sender, Message("another@iit.test"));
        var suffix = await FailAsync(sender, Message(Allowed + ".evil"));
        var lookalike = await FailAsync(sender, Message("x" + Allowed));

        Assert.Single(handler.Requests);
        Assert.All([domain, suffix, lookalike], e => Assert.Equal("RecipientNotAllowed", e.Code));
    }

    [Theory]
    [InlineData(400, EmailFailureKind.Permanent)]
    [InlineData(401, EmailFailureKind.Permanent)]
    [InlineData(403, EmailFailureKind.Permanent)]
    [InlineData(404, EmailFailureKind.Permanent)]
    [InlineData(408, EmailFailureKind.Retryable)]
    [InlineData(429, EmailFailureKind.Retryable)]
    [InlineData(500, EmailFailureKind.Retryable)]
    [InlineData(502, EmailFailureKind.Retryable)]
    [InlineData(503, EmailFailureKind.Retryable)]
    public async Task Provider_responses_map_to_retryable_or_permanent_with_a_safe_code(int status, EmailFailureKind expected)
    {
        var handler = new FakeBrevo(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent($"{{\"message\":\"rejected {Allowed} with key test-key-not-real\"}}"),
        }));

        var error = await FailAsync(Brevo(handler, Allowed), Message());

        Assert.Equal(expected, error.Kind);
        Assert.Equal($"Http{status}", error.Code);
        Assert.DoesNotContain(Allowed, error.Message + error.Code + error.ToString());
        Assert.DoesNotContain("test-key-not-real", error.Message + error.Code + error.ToString());
    }

    [Fact]
    public async Task A_timeout_is_Uncertain_because_the_provider_may_have_accepted_the_message()
    {
        var handler = new FakeBrevo(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        var error = await FailAsync(Brevo(handler, Allowed), Message());

        Assert.Equal(EmailFailureKind.Uncertain, error.Kind);
        Assert.Equal("Timeout", error.Code);
    }

    [Fact]
    public async Task A_connection_that_could_not_be_made_is_retryable_and_a_lost_connection_is_uncertain()
    {
        var refused = new FakeBrevo(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        var dropped = new FakeBrevo(_ => throw new HttpRequestException(HttpRequestError.ResponseEnded, "reset"));

        var notSent = await FailAsync(Brevo(refused, Allowed), Message());
        var unknown = await FailAsync(Brevo(dropped, Allowed), Message());

        Assert.Equal(EmailFailureKind.Retryable, notSent.Kind);
        Assert.Equal(EmailFailureKind.Uncertain, unknown.Kind);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_provider_failure()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeBrevo(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Brevo(handler, Allowed).SendAsync(Message(), cts.Token));
    }

    // --- Startup guards ---

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Test";

        public string ContentRootPath { get; set; } = string.Empty;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static ValidateOptionsResult Validate(string environment, string provider, string? apiKey = null, string? sender = null) =>
        new EmailProviderOptionsValidator(new Environment(environment), Options.Create(new BrevoOptions { ApiKey = apiKey, SenderEmail = sender }))
            .Validate(null, new EmailProviderOptions { Provider = provider });

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Brevo_is_refused_outside_Development(string environment)
    {
        var result = Validate(environment, "Brevo", "key", "sender@iit.test");

        Assert.True(result.Failed);
        Assert.Contains("Development-only", string.Join(" ", result.Failures!));
    }

    [Fact]
    public void A_Brevo_key_present_in_Production_is_refused_even_when_another_provider_is_selected()
    {
        var result = Validate("Production", "Smtp", apiKey: "leaked-development-key");

        Assert.True(result.Failed);
        Assert.DoesNotContain("leaked-development-key", string.Join(" ", result.Failures!));
    }

    [Fact]
    public void Production_may_use_a_different_provider()
    {
        Assert.True(Validate("Production", "Smtp").Succeeded);
    }

    [Fact]
    public void Development_needs_a_key_and_a_sender_to_use_Brevo_and_rejects_an_unknown_provider()
    {
        Assert.True(Validate("Development", "Brevo").Failed);
        Assert.True(Validate("Development", "Brevo", "key").Failed);
        Assert.True(Validate("Development", "Brevo", "key", "sender@iit.test").Succeeded);
        Assert.True(Validate("Development", "Smtp").Succeeded);
        Assert.True(Validate("Development", "Mailgun").Failed);
    }

    [Fact]
    public void The_provider_is_chosen_by_configuration_without_changing_what_producers_depend_on()
    {
        IEmailSender Resolve(string environment, string provider)
        {
            var settings = new Dictionary<string, string?> { ["Email:Provider"] = provider, ["Brevo:ApiKey"] = "key", ["Brevo:SenderEmail"] = "s@iit.test" };
            var services = new ServiceCollection();
            services.AddSingleton<IHostEnvironment>(new Environment(environment));
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            services.AddLogging();
            services.Configure<SmtpOptions>(_ => { });
            services.AddEmailProvider(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
        }

        Assert.IsType<BrevoEmailSender>(Resolve("Development", "Brevo"));
        Assert.IsType<SmtpEmailSender>(Resolve("Development", "Smtp"));
    }

    [Fact]
    public void A_host_started_in_Production_with_Brevo_selected_refuses_to_start()
    {
        using var factory = new ProductionBrevoHost();

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Development-only", Flatten(error));
    }

    private static string Flatten(Exception error)
    {
        var text = new List<string>();
        for (var e = error; e is not null; e = e.InnerException)
        {
            text.Add(e.Message);
            if (e is OptionsValidationException validation)
            {
                text.AddRange(validation.Failures);
            }

            if (e is AggregateException aggregate)
            {
                text.AddRange(aggregate.InnerExceptions.Select(Flatten));
            }
        }

        return string.Join(" | ", text);
    }

    private sealed class ProductionBrevoHost : PortalApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
            builder.UseSetting("Email:Provider", "Brevo");
            builder.UseSetting("Brevo:ApiKey", "development-key");
            builder.UseSetting("Brevo:SenderEmail", "sender@iit.test");
        }
    }

    // --- Provider substitution: the same producer request through different adapters ---

    private sealed class BrevoHost(FakeBrevo handler) : NotificationTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(_ => Brevo(handler, Allowed));
            });
        }
    }

    /// <summary>A stand-in for the approved Production provider: any adapter that implements the boundary.</summary>
    private sealed class SubstituteSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class SubstituteHost(SubstituteSender sender) : NotificationTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });
        }
    }

    private static IitAcademicPortal.Application.Notifications.NotificationEventRequest ResultsFor(string user) =>
        new(NotificationEventType.ResultsPublished, "publication:substitution", [user],
            new Dictionary<string, string> { ["courseName"] = "Compilers", ["termName"] = "Autumn 2031" });

    [Fact]
    public async Task The_same_producer_request_is_delivered_through_Brevo_in_Development_and_through_a_substitute_adapter_unchanged()
    {
        var handler = new FakeBrevo();
        using var brevoHost = new BrevoHost(handler);
        var brevoUser = await brevoHost.CreateUserAsync(Allowed, IitAcademicPortal.Domain.Identity.PortalRoles.Student);
        var substitute = new SubstituteSender();
        using var substituteHost = new SubstituteHost(substitute);
        var otherEmail = PortalApiFactory.UniqueEmail("student");
        var otherUser = await substituteHost.CreateUserAsync(otherEmail, IitAcademicPortal.Domain.Identity.PortalRoles.Student);

        await brevoHost.RequestAsync(ResultsFor(brevoUser));
        await brevoHost.FlushNotificationsAsync();
        await substituteHost.RequestAsync(ResultsFor(otherUser));
        await substituteHost.FlushNotificationsAsync();

        var request = Assert.Single(handler.Requests);
        var sentByBrevo = JsonDocument.Parse(request.Body).RootElement;
        var sentBySubstitute = Assert.Single(substitute.Sent);
        Assert.Equal(sentBySubstitute.Subject, sentByBrevo.GetProperty("subject").GetString());
        Assert.Equal(sentBySubstitute.Body, sentByBrevo.GetProperty("textContent").GetString());
        Assert.Equal(otherEmail, sentBySubstitute.To);
        Assert.Equal(NotificationStatus.Sent, (await brevoHost.StateAsync((await brevoHost.RecordsAsync()).Single().Id)).Status);
        Assert.Equal(NotificationStatus.Sent, (await substituteHost.StateAsync((await substituteHost.RecordsAsync()).Single().Id)).Status);
    }

    [Fact]
    public async Task A_destination_outside_the_Development_allowlist_fails_permanently_before_any_http_request_through_the_whole_pipeline()
    {
        var handler = new FakeBrevo();
        using var host = new BrevoHost(handler);
        var user = await host.CreateRecipientAsync();

        await host.RequestAsync(ResultsFor(user));
        await host.FlushNotificationsAsync();

        Assert.Empty(handler.Requests);
        var record = Assert.Single(await host.RecordsAsync());
        var state = await host.StateAsync(record.Id);
        Assert.Equal(NotificationStatus.Failed, state.Status);
        Assert.Equal("RecipientNotAllowed", state.FailureCode);
        Assert.Single(await host.AttemptsAsync(record.Id));
    }
}
