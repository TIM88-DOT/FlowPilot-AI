using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.Property(t => t.BusinessName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.BusinessPhone).HasMaxLength(20);
        builder.Property(t => t.BusinessEmail).HasMaxLength(200);
        builder.Property(t => t.Timezone).HasMaxLength(50);
        builder.Property(t => t.DefaultLanguage).HasMaxLength(10).HasDefaultValue("fr");

        builder.HasOne(t => t.Settings)
            .WithOne(s => s.Tenant)
            .HasForeignKey<TenantSettings>(s => s.OwnerTenantId);

        builder.HasOne(t => t.Plan)
            .WithOne(p => p.Tenant)
            .HasForeignKey<Plan>(p => p.TenantId);
    }
}
