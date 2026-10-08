using System.Security.Cryptography;
using System.Text;

namespace IitAcademicPortal.Application.Sessions;

/// <summary>
/// Creates opaque session handles and the one-way digests stored in place of them.
/// </summary>
/// <remarks>
/// The handle carries 256 bits of randomness, so looking up its SHA-256 digest by equality does not
/// leak useful timing information; the raw handle exists only in the browser cookie.
/// </remarks>
public static class SessionHandle
{
    public static string Create() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string ComputeDigest(string handle) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(handle)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
