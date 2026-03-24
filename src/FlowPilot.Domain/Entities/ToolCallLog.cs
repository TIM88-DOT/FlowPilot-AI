using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Log of a single tool call within an agent run.
/// </summary>
public class ToolCallLog : BaseEntity
{
    public Guid AgentRunId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public int DurationMs { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }

    public AgentRun AgentRun { get; set; } = null!;
}
