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

        builder.Property(ns => ns.ReporterId)
            .IsRequired();

        builder.Property(ns => ns.OffenderId)
            .IsRequired();

        builder.Property(ns => ns.RideId)
            .IsRequired();

        builder.Property(ns => ns.CreatedAt)
            .IsRequired();

        builder.HasIndex(ns => ns.OffenderId);
    }
}
