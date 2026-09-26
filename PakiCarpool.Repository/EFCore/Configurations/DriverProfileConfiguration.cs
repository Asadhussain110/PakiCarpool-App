using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class DriverProfileConfiguration : IEntityTypeConfiguration<DriverProfile>
{
    public void Configure(EntityTypeBuilder<DriverProfile> builder)
    {
        builder.ToTable("DriverProfiles");

        builder.HasKey(dp => dp.Id);

        builder.HasOne(dp => dp.User)
            .WithOne()
            .HasForeignKey<DriverProfile>(dp => dp.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(dp => dp.UserId)
            .IsUnique();

        builder.Property(dp => dp.LivenessVerified)
            .HasDefaultValue(false);

        builder.Property(dp => dp.ProfilePhotoPath)
            .HasMaxLength(500);

        builder.Property(dp => dp.LivenessFailCount)
            .HasDefaultValue(0);

        builder.Property(dp => dp.AverageRating)
            .HasPrecision(3, 2)
            .HasDefaultValue(0m);

        builder.Property(dp => dp.CreatedAt)
            .IsRequired();

        builder.HasMany(dp => dp.Cars)
            .WithOne(c => c.Driver)
            .HasForeignKey(c => c.DriverId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
