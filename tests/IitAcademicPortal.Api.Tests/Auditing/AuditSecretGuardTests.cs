using System.Text.Json.Nodes;
using IitAcademicPortal.Application.Auditing;

namespace IitAcademicPortal.Api.Tests.Auditing;

public sealed class AuditSecretGuardTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("NewPassword")]
    [InlineData("sessionHandle")]
    [InlineData("resetProof")]
    [InlineData("Authorization")]
    [InlineData("x-csrf-token")]
    [InlineData("api_key")]
    [InlineData("cookie")]
    public void Credential_like_names_are_sensitive(string name) => Assert.True(AuditSecretGuard.IsSensitiveName(name));

    [Theory]
    [InlineData("status")]
    [InlineData("route")]
    [InlineData("activeRole")]
    [InlineData("reason")]
    public void Ordinary_names_are_not_sensitive(string name) => Assert.False(AuditSecretGuard.IsSensitiveName(name));

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")] // 128-bit hex secret
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE")] // 256-bit base64url, like a session handle
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2lnbmF0dXJl")] // JWT
    public void Token_like_values_are_treated_as_secrets(string value) => Assert.True(AuditSecretGuard.LooksLikeSecret(value));

    [Theory]
    [InlineData("a short note")]
    [InlineData("api/auth/sessions/current/active-role")]
    [InlineData("test-probe/student-records/{studentUserId}")]
    [InlineData("2026-10-09T17:20:34.1234567Z")]
    [InlineData("11111111-2222-3333-4444-555555555555")] // a GUID identifier
    public void Identifiers_routes_and_ordinary_text_are_not_secrets(string value) => Assert.False(AuditSecretGuard.LooksLikeSecret(value));

    [Fact]
    public void Sanitizing_walks_nested_objects_and_arrays()
    {
        var node = JsonNode.Parse("""
            {"a":{"password":"p","keep":"ok"},"list":[{"token":"t"},"fine","0123456789abcdef0123456789abcdef"],"n":5}
            """);

        var clean = AuditSecretGuard.Sanitize(node)!;

        Assert.Equal(AuditSecretGuard.Redacted, clean["a"]!["password"]!.GetValue<string>());
        Assert.Equal("ok", clean["a"]!["keep"]!.GetValue<string>());
        Assert.Equal(AuditSecretGuard.Redacted, clean["list"]![0]!["token"]!.GetValue<string>());
        Assert.Equal("fine", clean["list"]![1]!.GetValue<string>());
        Assert.Equal(AuditSecretGuard.Redacted, clean["list"]![2]!.GetValue<string>());
        Assert.Equal(5, clean["n"]!.GetValue<int>());
    }
}
