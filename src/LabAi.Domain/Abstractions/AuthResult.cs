using LabAi.Domain.Entities;
using LabAi.Domain.Enums;

namespace LabAi.Domain.Abstractions;

/// <summary>
/// Outcome of a login attempt. Carries no credentials — safe to hand to the UI layer and to log.
/// </summary>
/// <remarks>
/// The identity here is what later becomes <c>ActorUserId</c> in the AI audit journal, so a login
/// that succeeds must resolve to a real row: an anonymous or synthetic actor would make the journal's
/// central claim ("who asked") unverifiable.
/// </remarks>
public sealed record AuthResult(
    bool Succeeded,
    string? Error,
    long UserId,
    string Username,
    string FullName,
    UserRole Role)
{
    public static AuthResult Success(User user)
        => new(true, null, user.Id, user.Username, user.FullName, user.Role);

    public static AuthResult Failure(string error)
        => new(false, error, 0, string.Empty, string.Empty, UserRole.Operator);
}
