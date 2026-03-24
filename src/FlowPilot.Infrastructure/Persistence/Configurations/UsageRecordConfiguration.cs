using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("usage_records");

        // One record per plan per month
        builder.HasIndex(u => new { u.PlanId, u.Year, u.Month }).IsUnique();

        builder.HasOne(u => u.Plan)
            .WithMany(p => p.UsageRecords)
            .HasForeignKey(u => u.PlanId);
    }
}
