using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");

        builder.HasKey(b => b.Id);

        builder.HasOne(b => b.Passenger)
            .WithMany()
            .HasForeignKey(b => b.PassengerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.PickupStop)
            .WithMany()
            .HasForeignKey(b => b.PickupStopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.DropoffStop)
            .WithMany()
            .HasForeignKey(b => b.DropoffStopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(b => b.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(b => b.RideId);
        builder.HasIndex(b => b.PassengerId, "IX_Bookings_PassengerId")
            .HasDatabaseName("IX_Bookings_PassengerId");
        builder.HasIndex(b => b.PassengerId, "ix_bookings_active_passenger")
            .IsUnique()
            .HasDatabaseName("ix_bookings_active_passenger")
            .HasFilter("\"Status\" IN ('Pending', 'Accepted', 'Active')");
        builder.HasIndex(b => b.Status);

        builder.Property(b => b.RequestedAt)
            .IsRequired();

        builder.Property(b => b.IsCashPaid)
            .HasDefaultValue(false);

        builder.Property(b => b.CancelReason)
            .HasMaxLength(500);

        builder.Property(b => b.CancelClassification)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasMany(b => b.ChatMessages)
            .WithOne(cm => cm.Booking)
            .HasForeignKey(cm => cm.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
