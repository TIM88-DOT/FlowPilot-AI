using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Log of an AI agent execution (e.g., ReminderOptimization, ReplyHandling, ReviewRecovery).
/// </summary>
public class AgentRun : BaseEntity
{
    public string AgentType { get; set; } = string.Empty;
    public Guid? AppointmentId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? TriggerEvent { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public int DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<ToolCallLog> ToolCallLogs { get; set; } = new List<ToolCallLog>();
}
