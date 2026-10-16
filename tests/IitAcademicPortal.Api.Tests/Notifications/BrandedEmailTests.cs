using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// Every notification is sent in one IIT-branded HTML layout (logo, heading, message, footer) with the plain text as its alternative.
/// The HTML is built in memory from the plain-text wording, so nothing an Admin or a producer supplies can become markup, and it is
/// never stored.
/// </summary>
public sealed class BrandedEmailTests : NotificationTestBase
{
    private const string Logo = "https://portal.example.org/images/iit-logo.png";

    // --- The layout ---

    [Fact]
    public void The_layout_has_the_logo_with_alt_text_a_heading_the_message_and_a_footer()
    {
        var html = EmailLayout.Render("Results published for Databases", "First paragraph.\n\nSecond line one\nSecond line two", new EmailBranding(LogoUrl: Logo));

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains($"<img src=\"{Logo}\" alt=\"IIT, University of Dhaka\"", html);
        Assert.Contains("<h1 ", html);
        Assert.Contains(">Results published for Databases</h1>", html);
        Assert.Contains("<title>Results published for Databases</title>", html);
        Assert.Contains(">First paragraph.</p>", html);
        Assert.Contains("Second line one<br>Second line two", html);
        Assert.Contains(EmailBranding.DefaultFooter, html);
        Assert.Contains("#2E3192", html);
        Assert.Contains("role=\"presentation\"", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("/images/iit-logo.png")]
    [InlineData("ftp://example.org/logo.png")]
    public void Without_a_usable_web_address_the_portal_name_is_shown_as_text_instead_of_an_image(string? logo)
    {
        var html = EmailLayout.Render("Subject", "Body", new EmailBranding(LogoUrl: logo));

        Assert.DoesNotContain("<img", html);
        Assert.Contains("IIT Academic Portal</span>", html);
    }

    [Fact]
    public void Supplied_text_can_never_become_markup()
    {
        var hostile = "<script>alert(1)</script> \"><img src=x onerror=alert(2)> <b>bold</b> &amp; {{x}}";

        var html = EmailLayout.Render(hostile, hostile, new EmailBranding(LogoUrl: Logo));

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("onerror=alert(2)>", html);
        Assert.DoesNotContain("<b>bold</b>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("&amp;amp;", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<img"));
    }

    [Fact]
    public void Only_whole_http_and_https_addresses_become_links_and_the_trailing_punctuation_stays_outside()
    {
        var html = EmailLayout.Render(
            "S", "See https://portal.example.org/a?b=1&c=2. Or javascript:alert(1) and data:text/html,x and www.example.org.", new EmailBranding(LogoUrl: Logo));

        Assert.Contains("<a href=\"https://portal.example.org/a?b=1&amp;c=2\"", html);
        Assert.Contains(">https://portal.example.org/a?b=1&amp;c=2</a>.", html);
        Assert.DoesNotContain("href=\"javascript", html);
        Assert.DoesNotContain("href=\"data", html);
        Assert.DoesNotContain("href=\"www", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<a "));
    }

    [Fact]
    public void An_address_on_its_own_line_becomes_a_button_with_the_address_repeated_as_text()
    {
        var link = "https://portal.example.org/reset-password?email=a%40iit.test&proof=ABC%2B123";

        var html = EmailLayout.Render("Reset", $"Use this link.\n\n{link}\n\nIgnore it if you did not ask.", new EmailBranding(LogoUrl: Logo));

        Assert.Contains("Open the portal link</a>", html);
        Assert.Contains("href=\"https://portal.example.org/reset-password?email=a%40iit.test&amp;proof=ABC%2B123\"", html);
        Assert.Contains("copy this address into your browser", html);
        Assert.Contains("proof=ABC%2B123</p>", html);
    }

    [Fact]
    public void A_nonfunctional_sample_link_is_never_linked()
    {
        var html = EmailLayout.Render("S", $"Link:\n\n{NotificationTemplateCatalog.SyntheticActionLink}", new EmailBranding(LogoUrl: Logo));

        Assert.Contains("[SAMPLE LINK - not functional]", html);
        Assert.DoesNotContain("href=", html);
    }

    [Fact]
    public void Windows_line_endings_and_extra_blank_lines_make_clean_paragraphs()
    {
        var html = EmailLayout.Render("S", "One\r\n\r\n\r\n\r\nTwo\r\nstill two\r\n", new EmailBranding(LogoUrl: Logo));

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<p style=\"margin:0 0 16px 0;font-size:16px").Count);
        Assert.Contains("Two<br>still two", html);
    }

    [Fact]
    public void The_logo_defaults_to_the_portals_own_image_and_can_be_overridden()
    {
        Assert.Equal("https://portal.example.org/images/iit-logo.png", new NotificationOptions { PortalUrl = "https://portal.example.org/" }.EffectiveLogoUrl);
        Assert.Equal("https://cdn.example.org/logo.png", new NotificationOptions { PortalUrl = "https://portal.example.org", LogoUrl = "https://cdn.example.org/logo.png" }.EffectiveLogoUrl);
        Assert.Equal("https://portal.example.org/images/iit-logo.png", new NotificationOptions { PortalUrl = "https://portal.example.org", LogoUrl = "  " }.EffectiveLogoUrl);
    }


    // --- Configurable branding ---

    [Fact]
    public void The_brand_color_portal_name_logo_text_footer_and_button_label_come_from_the_configured_branding()
    {
        var branding = new EmailBranding("Example Portal", null, "Example logo", "#0B6623", "Sent by Example. Do not reply.", "Open it");

        var html = EmailLayout.Render("Heading", "Words\n\nhttps://portal.example.org/go", branding);

        Assert.Contains("#0B6623", html);
        Assert.DoesNotContain("#2E3192", html);
        Assert.Contains("Example Portal</span>", html);
        Assert.Contains("Sent by Example. Do not reply.", html);
        Assert.Contains(">Open it</a>", html);
        Assert.DoesNotContain("Open the portal link", html);
        Assert.DoesNotContain("IIT Academic Portal", html);
    }

    [Fact]
    public void Configured_text_is_encoded_like_every_other_value_and_the_logo_alt_text_is_used()
    {
        var branding = new EmailBranding("<b>Name</b>", Logo, "Logo \"alt\" <i>", FooterText: "<script>x</script>", ButtonLabel: "<u>Go</u>");

        var html = EmailLayout.Render("S", "Body\n\nhttps://portal.example.org/go", branding);

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<u>Go</u>", html);
        Assert.Contains("alt=\"Logo &quot;alt&quot; &lt;i&gt;\"", html);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt;", html);
        Assert.Contains("&lt;u&gt;Go&lt;/u&gt;", html);
    }

    private static NotificationOptions Valid(Action<NotificationOptions>? change = null)
    {
        var options = new NotificationOptions { PortalUrl = "https://portal.example.org" };
        change?.Invoke(options);
        return options;
    }

    private static string[] Failures(NotificationOptions options) =>
        new NotificationOptionsValidator().Validate(null, options).Failures?.ToArray() ?? [];

    [Fact]
    public void The_defaults_and_a_dark_enough_custom_color_are_valid()
    {
        Assert.Empty(Failures(Valid()));
        Assert.Empty(Failures(Valid(o => o.BrandColor = "#0b6623")));
        Assert.Empty(Failures(Valid(o => o.LogoUrl = "https://cdn.example.org/logo.png")));
        Assert.True(NotificationOptionsValidator.ContrastWithWhite("#2E3192") > 4.5);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("#12345G")]
    [InlineData("#2E3192; background:url(x)")]
    [InlineData("")]
    public void A_brand_color_that_is_not_a_six_digit_hex_value_is_refused(string color)
    {
        Assert.Contains(Failures(Valid(o => o.BrandColor = color)), f => f.Contains("BrandColor"));
    }

    [Theory]
    [InlineData("#FFFF00")]
    [InlineData("#CCCCCC")]
    [InlineData("#FFFFFF")]
    public void A_brand_color_too_light_for_the_white_button_text_is_refused(string color)
    {
        Assert.Contains(Failures(Valid(o => o.BrandColor = color)), f => f.Contains("too light"));
    }

    [Theory]
    [InlineData("http://cdn.example.org/logo.png")]
    [InlineData("/images/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    public void A_logo_address_that_is_not_an_absolute_https_address_is_refused(string logo)
    {
        Assert.Contains(Failures(Valid(o => o.LogoUrl = logo)), f => f.Contains("LogoUrl"));
    }

    [Fact]
    public void Empty_over_long_or_multi_line_text_settings_are_refused()
    {
        Assert.Contains(Failures(Valid(o => o.PortalName = " ")), f => f.Contains("PortalName"));
        Assert.Contains(Failures(Valid(o => o.FooterText = new string('x', 301))), f => f.Contains("FooterText"));
        Assert.Contains(Failures(Valid(o => o.ButtonLabel = "Open\nit")), f => f.Contains("ButtonLabel"));
        Assert.Contains(Failures(Valid(o => o.LogoAlt = "")), f => f.Contains("LogoAlt"));
        Assert.Contains(Failures(Valid(o => o.PortalUrl = "not a url")), f => f.Contains("PortalUrl"));
    }

    private sealed class CustomBrandHost : NotificationTestHost
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Notifications:BrandColor", "#0B6623");
            builder.UseSetting("Notifications:PortalName", "Example Portal");
            builder.UseSetting("Notifications:FooterText", "Sent by Example.");
            builder.UseSetting("Notifications:ButtonLabel", "Open it");
            builder.UseSetting("Notifications:LogoUrl", "https://cdn.example.org/brand.png");
        }
    }

    private sealed class BadBrandHost : NotificationTestHost
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Notifications:BrandColor", "#FFFF00");
        }
    }

    [Fact]
    public async Task Configuration_changes_the_look_of_a_sent_email_and_of_the_preview_without_touching_the_wording()
    {
        using var host = new CustomBrandHost();
        var student = await host.CreateRecipientAsync();
        await host.RequestAsync(Results("brand-1", student));
        await host.FlushNotificationsAsync();

        var message = Assert.Single(host.Emails.Messages);

        Assert.Contains("#0B6623", message.HtmlBody);
        Assert.Contains("src=\"https://cdn.example.org/brand.png\"", message.HtmlBody);
        Assert.Contains("Sent by Example.", message.HtmlBody);
        Assert.Contains(">Open it</a>", message.HtmlBody);
        Assert.Contains("Results for Algorithms (Fall 2026) have been published.", message.Body);
        Assert.DoesNotContain("Example", message.Body);
    }

    [Fact]
    public void A_host_with_an_unusable_brand_color_refuses_to_start()
    {
        using var host = new BadBrandHost();

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains("BrandColor", Flatten(error));
    }

    private static string Flatten(Exception error)
    {
        var text = new List<string>();
        for (var e = error; e is not null; e = e.InnerException)
        {
            text.Add(e.Message);
            if (e is Microsoft.Extensions.Options.OptionsValidationException validation)
            {
                text.AddRange(validation.Failures);
            }
        }

        return string.Join(" | ", text);
    }

    // --- Sending ---

    [Fact]
    public async Task An_ordinary_message_is_sent_as_plain_text_with_the_branded_html_alternative_and_only_the_plain_text_is_stored()
    {
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("branded-1", student));

        await Host.FlushNotificationsAsync();

        var message = Assert.Single(Host.Emails.Messages);
        Assert.Contains("Results for Algorithms (Fall 2026) have been published.", message.Body);
        Assert.DoesNotContain("<", message.Body);
        Assert.NotNull(message.HtmlBody);
        Assert.Contains("<img src=\"https://portal.test/images/iit-logo.png\" alt=\"IIT, University of Dhaka\"", message.HtmlBody);
        Assert.Contains(">Results published for Algorithms</h1>", message.HtmlBody);
        Assert.Contains("Results for Algorithms (Fall 2026) have been published.", message.HtmlBody);
        Assert.Contains("Open the portal link</a>", message.HtmlBody);
        var record = (await Host.RecordsAsync()).Single(r => r.Id == id);
        Assert.DoesNotContain("<", record.Body);
        Assert.DoesNotContain("<", record.Subject);
    }

    [Fact]
    public async Task A_hostile_value_from_a_producer_is_encoded_in_the_html()
    {
        var student = await Host.CreateRecipientAsync();

        await Host.RequestAsync(new NotificationEventRequest(
            IitAcademicPortal.Domain.Notifications.NotificationEventType.ResultsPublished, "branded-2", [student],
            new Dictionary<string, string> { ["courseName"] = "<script>alert(1)</script>", ["termName"] = "\"><img onerror=x>" }));
        await Host.FlushNotificationsAsync();

        var message = Assert.Single(Host.Emails.Messages);
        Assert.DoesNotContain("<script", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(message.HtmlBody!, "<img"));
    }

    [Fact]
    public async Task A_security_message_carries_its_link_as_a_button_in_the_html_and_neither_version_is_stored_anywhere()
    {
        var email = PortalApiFactory.UniqueEmail("student");
        await Host.CreateUserAsync(email, PortalRoles.Student);
        using var client = Host.CreatePortalClient();
        var proof = await PasswordRecoveryTests.RequestProofAsync(Host, client, email);

        var message = Assert.Single(Host.Emails.SentTo(email));

        Assert.Contains("Open the portal link</a>", message.HtmlBody);
        Assert.Contains("href=\"https://portal.test/reset-password?email=", message.HtmlBody);
        Assert.Contains(Uri.EscapeDataString(proof).Replace("&", "&amp;"), message.HtmlBody);
        var stored = new List<string>();
        await Host.WithDbAsync(async db =>
        {
            stored.AddRange((await db.NotificationRecords.AsNoTracking().ToListAsync()).Select(r => $"{r.Subject}{r.Body}{r.OccurrenceId}"));
            stored.AddRange((await db.NotificationAttempts.AsNoTracking().ToListAsync()).Select(a => a.FailureCode ?? string.Empty));
            stored.AddRange((await db.NotificationDispatchStates.AsNoTracking().ToListAsync()).Select(s => s.FailureCode ?? string.Empty));
            stored.AddRange((await db.AuditEvents.AsNoTracking().ToListAsync()).Select(e => $"{e.MetadataJson}{e.ChangesJson}"));
        });
        Assert.All(stored, text =>
        {
            Assert.DoesNotContain(proof, text);
            Assert.DoesNotContain("<html", text, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain("<html", Host.Logs.Everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(proof, Host.Logs.Everything);
    }

    // --- Brevo ---

    private sealed class Capture : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task Brevo_receives_the_html_and_the_plain_text_together_and_only_the_plain_text_when_there_is_no_html()
    {
        var handler = new Capture();
        var sender = new BrevoEmailSender(
            new HttpClient(handler),
            Options.Create(new BrevoOptions { ApiKey = "k", SenderEmail = "s@iit.test", AllowedRecipients = ["to@iit.test"] }));

        await sender.SendAsync(new EmailMessage("to@iit.test", "Subject", "Plain body", "<html>Branded</html>"), CancellationToken.None);
        var both = JsonDocument.Parse(handler.Body!).RootElement;
        await sender.SendAsync(new EmailMessage("to@iit.test", "Subject", "Plain body"), CancellationToken.None);
        var plainOnly = JsonDocument.Parse(handler.Body!).RootElement;

        Assert.Equal("Plain body", both.GetProperty("textContent").GetString());
        Assert.Equal("<html>Branded</html>", both.GetProperty("htmlContent").GetString());
        Assert.Equal("Plain body", plainOnly.GetProperty("textContent").GetString());
        Assert.False(plainOnly.TryGetProperty("htmlContent", out _));
    }

    // --- Preview ---

    private static string Route(string slug) => $"{Templates}/{slug}/email/previews";

    [Fact]
    public async Task A_preview_returns_the_branded_html_made_with_sample_data_and_nothing_is_sent()
    {
        using var admin = await AdminAsync();

        var (status, body) = await SendAsync(admin, HttpMethod.Post, Route("results-published"),
            new { subject = "Results for {{courseName}}", body = "{{courseName}} / {{termName}}", sampleValues = new { courseName = "<b>CS 101</b>" } });

        Assert.Equal(HttpStatusCode.OK, status);
        var html = body.GetProperty("html").GetString()!;
        Assert.Contains("<img src=\"https://portal.test/images/iit-logo.png\"", html);
        Assert.Contains("&lt;b&gt;CS 101&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>CS 101</b>", html);
        Assert.Contains("Sample Term", html);
        await Host.FlushNotificationsAsync();
        Assert.Equal(0, Host.Emails.Attempts);
        Assert.Empty(await Host.RecordsAsync());
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("teacher-invitation")]
    [InlineData("student-invitation")]
    [InlineData("email-confirmation")]
    public async Task A_security_preview_html_has_only_the_nonfunctional_link_placeholder_and_no_link_at_all(string slug)
    {
        using var admin = await AdminAsync();
        var (_, current) = await SendAsync(admin, HttpMethod.Get, $"{Templates}/{slug}/email");

        var (status, body) = await SendAsync(admin, HttpMethod.Post, Route(slug), new
        {
            subject = current.GetProperty("subject").GetString(),
            body = current.GetProperty("body").GetString(),
            sampleValues = new { actionLink = "https://evil.example/reset?proof=REAL" },
        });

        Assert.Equal(HttpStatusCode.OK, status);
        var html = body.GetProperty("html").GetString()!;
        Assert.Contains("[SAMPLE LINK - not functional]", html);
        Assert.DoesNotContain("evil.example", html);
        Assert.DoesNotContain("proof=", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("Open the portal link", html);
    }
}
