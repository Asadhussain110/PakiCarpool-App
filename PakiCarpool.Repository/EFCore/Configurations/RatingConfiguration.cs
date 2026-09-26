using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable("Ratings");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RideId)
            .IsRequired();

        builder.Property(r => r.RaterId)
            .IsRequired();

        builder.Property(r => r.RateeId)
            .IsRequired();

        builder.Property(r => r.Stars)
            .IsRequired();

        builder.Property(r => r.ReviewText)
            .HasMaxLength(300);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // One rating per direction per ride (e.g., driver→passenger, passenger→driver)
        builder.HasIndex(r => new { r.RideId, r.RaterId, r.RateeId })
            .IsUnique();
    }
}
