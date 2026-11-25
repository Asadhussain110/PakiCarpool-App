using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class FlagConfiguration : IEntityTypeConfiguration<Flag>
{
    public void Configure(EntityTypeBuilder<Flag> builder)
    {
        builder.ToTable("Flags");

        builder.HasKey(f => f.Id);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.ReporteeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ride>()
            .WithMany()
            .HasForeignKey(f => f.RideId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(f => f.ReporterId)
            .IsRequired();

        builder.Property(f => f.ReporteeId)
            .IsRequired();

        builder.Property(f => f.RideId)
            .IsRequired();

        builder.Property(f => f.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(f => f.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        builder.HasIndex(f => f.Status);
    }
}
