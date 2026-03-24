using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");

        builder.Property(a => a.ExternalId).HasMaxLength(200);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ServiceName).HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        // Idempotency: ExternalId + TenantId unique constraint for webhook ingestion
        builder.HasIndex(a => new { a.ExternalId, a.TenantId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");

        builder.HasOne(a => a.Customer)
            .WithMany(c => c.Appointments)
            .HasForeignKey(a => a.CustomerId);

        builder.HasOne(a => a.StaffUser)
            .WithMany()
            .HasForeignKey(a => a.StaffUserId)
            .IsRequired(false);
    }
}
