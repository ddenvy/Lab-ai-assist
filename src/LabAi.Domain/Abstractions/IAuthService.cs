namespace LabAi.Domain.Abstractions;

/// <summary>
/// Verifies credentials and resolves them to the identity that every audit row is attributed to.
/// </summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default);
}
