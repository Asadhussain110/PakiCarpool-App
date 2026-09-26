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
