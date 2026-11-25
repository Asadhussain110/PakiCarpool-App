using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class CancellationLogConfiguration : IEntityTypeConfiguration<CancellationLog>
{
    public void Configure(EntityTypeBuilder<CancellationLog> builder)
    {
        builder.ToTable("CancellationLogs");

        builder.HasKey(cl => cl.Id);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(cl => cl.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(cl => cl.UserId)
            .IsRequired();

        builder.Property(cl => cl.EntityId)
            .IsRequired();

        // Polymorphic: "Booking" or "Ride"
        builder.Property(cl => cl.EntityType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(cl => cl.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(cl => cl.Classification)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(cl => cl.TrustPointsDeducted)
            .IsRequired();

        builder.Property(cl => cl.CreatedAt)
            .IsRequired();

        builder.HasIndex(cl => cl.UserId);
    }
}
