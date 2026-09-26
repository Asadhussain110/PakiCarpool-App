using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Repository.EFCore.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");

        builder.HasKey(cm => cm.Id);

        builder.Property(cm => cm.SenderId)
            .IsRequired();

        builder.Property(cm => cm.ReceiverId)
            .IsRequired();

        builder.Property(cm => cm.Message)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(cm => cm.SentAt)
            .IsRequired();

        builder.Property(cm => cm.ExpiresAt)
            .IsRequired();

        builder.Property(cm => cm.IsDelivered)
            .HasDefaultValue(false);

        builder.HasIndex(cm => cm.BookingId);

        // Index on ExpiresAt for ChatExpiryJob background cleanup
        builder.HasIndex(cm => cm.ExpiresAt);
    }
}
