using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class ScheduledMessageConfiguration : IEntityTypeConfiguration<ScheduledMessage>
{
    public void Configure(EntityTypeBuilder<ScheduledMessage> builder)
    {
        builder.ToTable("scheduled_messages");

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.RenderedBody).HasMaxLength(1600);
        builder.Property(s => s.Locale).HasMaxLength(10);

        // Index on ScheduledAt for the dispatch worker query
        builder.HasIndex(s => s.ScheduledAt);

        // Composite index for finding pending messages by appointment
        builder.HasIndex(s => new { s.AppointmentId, s.Status });

        builder.HasOne(s => s.Appointment)
            .WithMany(a => a.ScheduledMessages)
            .HasForeignKey(s => s.AppointmentId);

        builder.HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId);

        builder.HasOne(s => s.Template)
            .WithMany()
            .HasForeignKey(s => s.TemplateId)
            .IsRequired(false);
    }
}
