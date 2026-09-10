using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Infrastructure.Persistence;

public sealed class EfUserStore(LabAiDbContext db) : IUserStore
{
    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default)
        => db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);
}
