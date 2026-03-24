using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.Property(m => m.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Body).IsRequired();
        builder.Property(m => m.FromPhone).HasMaxLength(20);
        builder.Property(m => m.ToPhone).HasMaxLength(20);
        builder.Property(m => m.ProviderSmsSid).HasMaxLength(100);
        builder.Property(m => m.ProviderMessageId).HasMaxLength(100);

        // Idempotency: ProviderSmsSid for inbound dedup
        builder.HasIndex(m => m.ProviderSmsSid)
            .IsUnique()
            .HasFilter("provider_sms_sid IS NOT NULL");

        builder.HasOne(m => m.Customer)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.CustomerId);
    }
}
