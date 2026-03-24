using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");

        builder.Property(e => e.EventId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(200).IsRequired();

        // Idempotency: unique on EventId
        builder.HasIndex(e => e.EventId).IsUnique();
    }
}
