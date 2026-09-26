using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class RideConfiguration : IEntityTypeConfiguration<Ride>
{
    public void Configure(EntityTypeBuilder<Ride> builder)
    {
        builder.ToTable("Rides");

        builder.HasKey(r => r.Id);

        builder.HasOne(r => r.Driver)
            .WithMany()
            .HasForeignKey(r => r.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Car)
            .WithMany()
            .HasForeignKey(r => r.CarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Fare)
            .IsRequired()
            .HasPrecision(10, 2);

        builder.Property(r => r.MaxSeats)
            .IsRequired();

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(r => new { r.DriverId, r.Status });

        builder.Property(r => r.CancelGraceExpiresAt)
            .IsRequired();

        builder.Property(r => r.CancelReason)
            .HasMaxLength(500);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.HasMany(r => r.Stops)
            .WithOne(s => s.Ride)
            .HasForeignKey(s => s.RideId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Bookings)
            .WithOne(b => b.Ride)
            .HasForeignKey(b => b.RideId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
