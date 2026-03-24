using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.OldValue).HasColumnType("jsonb");
        builder.Property(a => a.NewValue).HasColumnType("jsonb");

        builder.HasOne(a => a.Appointment)
            .WithMany(apt => apt.AuditLogs)
            .HasForeignKey(a => a.AppointmentId)
            .IsRequired(false);
    }
}
