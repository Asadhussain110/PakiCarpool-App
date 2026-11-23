using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(u => u.Email)
            .IsUnique();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500);

        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(u => u.GoogleId)
            .HasMaxLength(255);

        builder.HasIndex(u => u.GoogleId)
            .IsUnique()
            .HasFilter("\"GoogleId\" IS NOT NULL");

        builder.Property(u => u.IsActive)
            .HasDefaultValue(true);

        builder.Property(u => u.IsSuspended)
            .HasDefaultValue(false);

        builder.Property(u => u.TrustScore)
            .HasDefaultValue(100);

        builder.Property(u => u.FailedLoginCount)
            .HasDefaultValue(0);

        builder.Property(u => u.LoginFailCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(u => u.AdminPasswordResetAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(u => u.PasswordResetAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(u => u.PasswordResetRequestCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(u => u.AdminPasswordResetRequestCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(u => u.AuthTokenVersion)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(u => u.EmailOtp)
            .HasMaxLength(500);

        builder.Property(u => u.CreatedAt)
            .IsRequired();
    }
}
