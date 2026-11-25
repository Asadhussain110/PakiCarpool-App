using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class RideStopConfiguration : IEntityTypeConfiguration<RideStop>
{
    public void Configure(EntityTypeBuilder<RideStop> builder)
    {
        builder.ToTable("RideStops", t =>
        {
            t.HasCheckConstraint("CK_RideStops_Latitude", "\"Latitude\" >= -90 AND \"Latitude\" <= 90");
            t.HasCheckConstraint("CK_RideStops_Longitude", "\"Longitude\" >= -180 AND \"Longitude\" <= 180");
        });

        builder.HasKey(rs => rs.Id);

        builder.Property(rs => rs.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(rs => rs.Latitude)
            .IsRequired();

        builder.Property(rs => rs.Longitude)
            .IsRequired();

        builder.Property(rs => rs.SequenceOrder)
            .IsRequired();

        builder.Property(rs => rs.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired()
            .HasDefaultValue(RideStopStatus.Upcoming);

        builder.HasIndex(rs => new { rs.RideId, rs.SequenceOrder });

        builder.HasIndex(rs => new { rs.RideId, rs.SequenceOrder })
            .IsUnique()
            .HasDatabaseName("IX_RideStops_RideId_SequenceOrder_Unique");
    }
}
