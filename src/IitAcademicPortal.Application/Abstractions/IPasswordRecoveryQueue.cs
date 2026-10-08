namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// Defers recovery processing so the public response does not depend on whether an account exists,
/// either in content or in response time.
/// </summary>
public interface IPasswordRecoveryQueue
{
    void Enqueue(string email);
}
