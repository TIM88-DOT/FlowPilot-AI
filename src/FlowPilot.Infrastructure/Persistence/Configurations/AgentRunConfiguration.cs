using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_runs");

        builder.Property(a => a.AgentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.TriggerEvent).HasMaxLength(200);
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.ErrorMessage).HasMaxLength(2000);
    }
}
