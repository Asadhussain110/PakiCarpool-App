using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class DriverLocationConfiguration : IEntityTypeConfiguration<DriverLocation>
{
    public void Configure(EntityTypeBuilder<DriverLocation> builder)
    {
        builder.ToTable("DriverLocations");

        builder.HasKey(dl => dl.Id);

        builder.Property(dl => dl.Latitude)
            .IsRequired();

        builder.Property(dl => dl.Longitude)
            .IsRequired();

        builder.Property(dl => dl.RecordedAt)
            .IsRequired();

        // Unique index enforces upsert pattern: one active record per driver per ride
        builder.HasIndex(dl => new { dl.DriverId, dl.RideId })
            .IsUnique();
    }
}
