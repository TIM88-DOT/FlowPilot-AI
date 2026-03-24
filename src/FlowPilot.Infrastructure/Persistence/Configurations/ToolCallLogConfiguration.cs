using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class ToolCallLogConfiguration : IEntityTypeConfiguration<ToolCallLog>
{
    public void Configure(EntityTypeBuilder<ToolCallLog> builder)
    {
        builder.ToTable("tool_call_logs");

        builder.Property(t => t.ToolName).HasMaxLength(100).IsRequired();
        builder.Property(t => t.InputJson).HasColumnType("jsonb");
        builder.Property(t => t.OutputJson).HasColumnType("jsonb");
        builder.Property(t => t.ErrorMessage).HasMaxLength(2000);

        builder.HasOne(t => t.AgentRun)
            .WithMany(a => a.ToolCallLogs)
            .HasForeignKey(t => t.AgentRunId);
    }
}
