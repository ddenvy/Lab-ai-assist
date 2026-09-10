using LabAi.Domain.Entities;

namespace LabAi.Domain.Abstractions;

/// <summary>
/// Read access to user accounts for authentication. Deliberately minimal: login only needs a lookup
/// by username. Mini-CDS also exposes a system-account id here, because its audit trail needs a
/// non-interactive author for system events; this journal has none — every row is written by a
/// logged-in person — so the method does not exist.
/// </summary>
public interface IUserStore
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);
}
