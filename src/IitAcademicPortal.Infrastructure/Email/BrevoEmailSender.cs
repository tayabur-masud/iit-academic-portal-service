using System.Net;
using System.Net.Http.Json;
using IitAcademicPortal.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Email;

/// <summary>
/// Sends one message per request through Brevo's transactional email endpoint (<c>POST /v3/smtp/email</c>) for Development. It is
/// not an SDK: it uses the standard HTTP client. It never logs or records credentials, addresses, or content, never calls Brevo's
/// message-content retrieval, and rejects any destination outside the explicit allowlist before an HTTP request is made.
/// </summary>
public sealed class BrevoEmailSender(HttpClient http, IOptions<BrevoOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        // Fail closed: an empty list allows nothing, and the check happens before any request exists.
        if (!settings.AllowedRecipients.Contains(message.To, StringComparer.OrdinalIgnoreCase))
        {
            throw new EmailDeliveryException(EmailFailureKind.Permanent, "RecipientNotAllowed");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseAddress, UriKind.Absolute), "v3/smtp/email"))
        {
            Content = JsonContent.Create(new Dictionary<string, object>
            {
                ["sender"] = new { name = settings.SenderName, email = settings.SenderEmail },
                ["to"] = new[] { new { email = message.To } },
                ["subject"] = message.Subject,
                ["textContent"] = message.Body,
            }.WithHtml(message.HtmlBody)),
        };
        request.Headers.Add("api-key", settings.ApiKey);
        request.Headers.Add("accept", "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            // The request may have reached Brevo before the timeout, so acceptance is unknown.
            throw new EmailDeliveryException(EmailFailureKind.Uncertain, "Timeout", ex);
        }
        catch (HttpRequestException ex)
        {
            // No response: a connection that could not be made, or one lost mid-flight.
            throw new EmailDeliveryException(
                ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError
                    ? EmailFailureKind.Retryable
                    : EmailFailureKind.Uncertain,
                "ProviderUnreachable", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var code = $"Http{status}";
            throw new EmailDeliveryException(Classify(response.StatusCode), code);
        }
    }

    private static EmailFailureKind Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => EmailFailureKind.Retryable,
        >= HttpStatusCode.InternalServerError => EmailFailureKind.Retryable,
        _ => EmailFailureKind.Permanent,
    };
}

internal static class BrevoRequestExtensions
{
    /// <summary>Adds the HTML version only when there is one; Brevo then sends both as alternatives.</summary>
    public static Dictionary<string, object> WithHtml(this Dictionary<string, object> request, string? html)
    {
        if (!string.IsNullOrEmpty(html))
        {
            request["htmlContent"] = html;
        }

        return request;
    }
}
