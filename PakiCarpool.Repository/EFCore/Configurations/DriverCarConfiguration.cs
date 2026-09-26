using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class DriverCarConfiguration : IEntityTypeConfiguration<DriverCar>
{
    public void Configure(EntityTypeBuilder<DriverCar> builder)
    {
        builder.ToTable("DriverCars", t =>
        {
            t.HasCheckConstraint("CK_DriverCars_AvailableSeats", "\"AvailableSeats\" >= 1 AND \"AvailableSeats\" <= 6");
        });

        builder.HasKey(dc => dc.Id);

        builder.Property(dc => dc.Make)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(dc => dc.Model)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(dc => dc.Year)
            .IsRequired();

        // LicensePlate: AES-256 encryption converter is applied in DbContext.OnModelCreating
        builder.Property(dc => dc.LicensePlate)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(dc => dc.AvailableSeats)
            .IsRequired();

        builder.Property(dc => dc.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(dc => dc.PhotoPaths)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(dc => dc.RegDocPath)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(dc => dc.RejectionReason)
            .HasMaxLength(500);

        builder.Property(dc => dc.CreatedAt)
            .IsRequired();
    }
}
