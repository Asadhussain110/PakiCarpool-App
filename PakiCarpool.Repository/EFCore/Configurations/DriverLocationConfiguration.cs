using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class DriverLocationConfiguration : IEntityTypeConfiguration<DriverLocation>
{
    public void Configure(EntityTypeBuilder<DriverLocation> builder)
    {
        builder.ToTable("DriverLocations", t =>
        {
            t.HasCheckConstraint("CK_DriverLocations_Latitude", "\"Latitude\" >= -90 AND \"Latitude\" <= 90");
            t.HasCheckConstraint("CK_DriverLocations_Longitude", "\"Longitude\" >= -180 AND \"Longitude\" <= 180");
        });

        builder.HasKey(dl => dl.Id);

        builder.HasOne<DriverProfile>()
            .WithMany()
            .HasForeignKey(dl => dl.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ride>()
            .WithMany()
            .HasForeignKey(dl => dl.RideId)
            .OnDelete(DeleteBehavior.Restrict);

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
