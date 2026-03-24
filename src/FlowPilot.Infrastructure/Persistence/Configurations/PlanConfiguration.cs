using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");

        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.FeatureFlags).HasColumnType("jsonb").HasDefaultValue("{}");
    }
}
