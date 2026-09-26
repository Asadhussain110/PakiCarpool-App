using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class RideStopConfiguration : IEntityTypeConfiguration<RideStop>
{
    public void Configure(EntityTypeBuilder<RideStop> builder)
    {
        builder.ToTable("RideStops");

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

        builder.Property(rs => rs.IsCompleted)
            .HasDefaultValue(false);

        builder.HasIndex(rs => new { rs.RideId, rs.SequenceOrder });

        builder.HasIndex(rs => new { rs.RideId, rs.SequenceOrder })
            .IsUnique()
            .HasDatabaseName("IX_RideStops_RideId_SequenceOrder_Unique");
    }
}
