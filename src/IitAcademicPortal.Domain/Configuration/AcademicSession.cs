namespace IitAcademicPortal.Domain.Configuration;

/// <summary>An academic session label, for example 2025-2026. Distinct from the authentication <c>AuthSession</c>; Batches may share one.</summary>
public sealed class AcademicSession : ConfigurationRecord;
