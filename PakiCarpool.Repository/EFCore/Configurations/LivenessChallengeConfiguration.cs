using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

/// <summary>
/// Fluent API configuration for the <see cref="LivenessChallenge"/> entity.
/// </summary>
public class LivenessChallengeConfiguration : IEntityTypeConfiguration<LivenessChallenge>
{
    public void Configure(EntityTypeBuilder<LivenessChallenge> builder)
    {
        builder.ToTable("LivenessChallenges");

        builder.HasKey(lc => lc.Id);

        builder.HasOne(lc => lc.Driver)
            .WithMany()
            .HasForeignKey(lc => lc.DriverId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(lc => lc.Nonce)
            .IsRequired()
            .HasMaxLength(88); // Base64Url of 64 bytes ≤ 88 chars

        builder.Property(lc => lc.PromptsJson)
            .IsRequired()
            .HasMaxLength(256)
            .HasDefaultValue("[]");

        builder.Property(lc => lc.IssuedAt)
            .IsRequired();

        builder.Property(lc => lc.ExpiresAt)
            .IsRequired();

        builder.Property(lc => lc.IsUsed)
            .HasDefaultValue(false);

        // Fast nonce look-up on submission
        builder.HasIndex(lc => lc.Nonce).IsUnique();

        // Fast per-driver query (e.g. invalidate stale challenges)
        builder.HasIndex(lc => lc.DriverId);

        // Supports the cleanup job filter (ExpiresAt < now OR IsUsed = true)
        builder.HasIndex(lc => lc.ExpiresAt);
    }
}
