using LabAi.Domain.Abstractions;

namespace LabAi.Application.Auth;

/// <summary>
/// Authenticates a user against the store. Deliberately free of frameworks and of any journal:
/// the identity it returns is what the AI audit log later attributes a question to, so this class has
/// exactly one job — decide whether that identity is real.
/// </summary>
public sealed class AuthService(IUserStore userStore, IPasswordHasher passwordHasher) : IAuthService
{
    /// <summary>
    /// One message for every failure. Distinguishing "no such user" from "wrong password" would let an
    /// unauthenticated caller enumerate the laboratory's staff list.
    /// </summary>
    private const string GenericError = "Invalid username or password.";

    /// <summary>
    /// A random secret hashed once per process. Unknown-user and disabled-account attempts verify
    /// against it, so all three failure paths burn the same PBKDF2 budget and response time cannot be
    /// used to enumerate users. The result is always discarded.
    /// </summary>
    private readonly Lazy<(string hash, string salt)> _dummyCredentials =
        new(() => passwordHasher.Hash(Guid.NewGuid().ToString("N")));

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await userStore.FindByUsernameAsync(username, ct).ConfigureAwait(false);

        // Fail closed, and fail identically: a disabled account is never even offered the chance to
        // match its real hash.
        if (user is null || !user.IsActive)
        {
            passwordHasher.Verify(password, _dummyCredentials.Value.hash, _dummyCredentials.Value.salt);
            return AuthResult.Failure(GenericError);
        }

        if (!passwordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
            return AuthResult.Failure(GenericError);

        return AuthResult.Success(user);
    }
}
