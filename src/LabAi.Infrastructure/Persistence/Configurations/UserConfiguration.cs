using LabAi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabAi.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Username).HasMaxLength(64).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(128).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(128).IsRequired();
        builder.Property(u => u.PasswordSalt).HasMaxLength(64).IsRequired();

        // Enums as text: forensic-readable audit exports and hand-inspection of the DB.
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);

        // SQLite returns DateTime with Kind=Unspecified — normalize back to Utc.
        builder.Property(u => u.CreatedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        builder.HasIndex(u => u.Username).IsUnique();
    }
}
