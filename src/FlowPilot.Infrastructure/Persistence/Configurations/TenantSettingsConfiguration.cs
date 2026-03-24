using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("tenant_settings");

        builder.Property(s => s.GooglePlaceId).HasMaxLength(200);
        builder.Property(s => s.FacebookPageUrl).HasMaxLength(500);
        builder.Property(s => s.TrustpilotUrl).HasMaxLength(500);
        builder.Property(s => s.BusinessHoursJson).HasColumnType("jsonb");
        builder.Property(s => s.DefaultSenderPhone).HasMaxLength(20);
    }
}
