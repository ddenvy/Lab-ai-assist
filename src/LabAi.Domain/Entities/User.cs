using LabAi.Domain.Enums;

namespace LabAi.Domain.Entities;

/// <summary>
/// Laboratory operator account. Provides the real identity behind <c>ActorUserId</c> in the AI audit
/// journal — an audit trail whose actor cannot be resolved to a person is declarative, not evidence.
/// </summary>
public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
