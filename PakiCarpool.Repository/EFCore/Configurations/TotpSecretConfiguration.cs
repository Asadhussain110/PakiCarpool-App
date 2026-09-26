using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class TotpSecretConfiguration : IEntityTypeConfiguration<TotpSecret>
{
    public void Configure(EntityTypeBuilder<TotpSecret> builder)
    {
        builder.ToTable("TotpSecrets");

        builder.HasKey(ts => ts.Id);

        builder.HasOne(ts => ts.User)
            .WithMany()
            .HasForeignKey(ts => ts.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Secret: AES-256 encryption converter is applied in DbContext.OnModelCreating
        builder.Property(ts => ts.Secret)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(ts => ts.IsVerified)
            .HasDefaultValue(false);

        // One TOTP secret per admin user
        builder.HasIndex(ts => ts.UserId)
            .IsUnique();
    }
}
