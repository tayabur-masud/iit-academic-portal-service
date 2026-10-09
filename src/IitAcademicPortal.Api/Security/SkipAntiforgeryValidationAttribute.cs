namespace IitAcademicPortal.Api.Security;

/// <summary>
/// Exempts an action from the global anti-forgery filter. Use it only on actions that change nothing, such as
/// the audit mutation guard: without it, an attempt lacking a CSRF token would be rejected with HTTP 400
/// before it could be recorded.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SkipAntiforgeryValidationAttribute : Attribute;
