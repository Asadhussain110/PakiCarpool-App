using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class NoShowLogConfiguration : IEntityTypeConfiguration<NoShowLog>
{
    public void Configure(EntityTypeBuilder<NoShowLog> builder)
    {
        builder.ToTable("NoShowLogs");

        builder.HasKey(ns => ns.Id);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ns => ns.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ns => ns.OffenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ride>()
            .WithMany()
            .HasForeignKey(ns => ns.RideId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(ns => ns.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(ns => ns.ReporterId)
            .IsRequired();

        builder.Property(ns => ns.OffenderId)
            .IsRequired();

        builder.Property(ns => ns.RideId)
            .IsRequired();

        builder.Property(ns => ns.BookingId);

        builder.Property(ns => ns.CreatedAt)
            .IsRequired();

        builder.HasIndex(ns => ns.OffenderId);

        // One no-show per booking — the database-level guarantee that repeated
        // or concurrent reports for the same booking cannot double-penalize.
        // Nullable for legacy rows, which PostgreSQL unique indexes exclude (multiple NULLs allowed).
        builder.HasIndex(ns => ns.BookingId)
            .IsUnique();
    }
}
