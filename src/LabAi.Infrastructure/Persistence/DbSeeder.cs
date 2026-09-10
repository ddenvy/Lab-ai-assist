using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Infrastructure.Persistence;

/// <summary>A user created by this seeding pass. <see cref="Password"/> is null when it came from configuration.</summary>
public sealed record SeededUser(string Username, long Id, string? Password);

/// <summary>Outcome of one seeding pass.</summary>
public sealed record SeedResult(IReadOnlyList<SeededUser> Created);

/// <summary>
/// Idempotently creates the demo accounts. Existing users are never modified and never re-hashed: a
/// row already in the database is a record, and rewriting it on every start would destroy the link
/// between an audit entry's <c>ActorUserId</c> and the credentials that were actually in force.
/// </summary>
/// <remarks>
/// Unlike Mini-CDS this seeds no non-interactive <c>system</c> account. That account exists there to
/// author system-generated audit events; every row in this journal is written by a person who logged in,
/// so there is nothing for a synthetic author to sign.
/// </remarks>
public sealed class DbSeeder(LabAiDbContext db, IPasswordHasher passwordHasher)
{
    /// <summary>
    /// When <paramref name="demoPassword"/> is null a random password is generated per created user and
    /// returned in the result — it is never persisted in plaintext and never logged here.
    /// </summary>
    public async Task<SeedResult> SeedAsync(string? demoPassword, CancellationToken ct = default)
    {
        var existing = await db.Users.Select(u => u.Username).ToListAsync(ct).ConfigureAwait(false);
        var created = new List<SeededUser>();

        foreach (var (username, fullName, role) in DemoAccounts())
        {
            if (existing.Contains(username, StringComparer.OrdinalIgnoreCase))
                continue;

            var password = demoPassword ?? GeneratePassword();
            var (hash, salt) = passwordHasher.Hash(password);

            var user = new User
            {
                Username = username,
                FullName = fullName,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = role,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Report the plaintext only when we generated it; a configured password is already known
            // to whoever configured it and echoing it would put a secret in the startup output.
            created.Add(new SeededUser(username, user.Id, demoPassword is null ? password : null));
        }

        return new SeedResult(created);
    }

    /// <summary>
    /// One account per role, so the demo can show role-based access from both sides: only
    /// <see cref="UserRole.Analyst"/> and <see cref="UserRole.Administrator"/> may ingest, and only an
    /// administrator sees other actors' audit rows.
    /// </summary>
    private static IEnumerable<(string Username, string FullName, UserRole Role)> DemoAccounts()
    {
        yield return ("admin", "Demo Administrator", UserRole.Administrator);
        yield return ("analyst", "Demo Analyst", UserRole.Analyst);
        yield return ("operator", "Demo Operator", UserRole.Operator);
    }

    private static string GeneratePassword()
    {
        // Alphabet excludes the characters that are ambiguous on a printed console: l 1 I O 0 o.
        const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
