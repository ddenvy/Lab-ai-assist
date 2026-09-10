namespace LabAi.Domain.Abstractions;

/// <summary>
/// Password hashing contract. Implementation in Infrastructure (PBKDF2-SHA256).
/// </summary>
public interface IPasswordHasher
{
    (string hash, string salt) Hash(string password);

    bool Verify(string password, string hash, string salt);
}
