using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Monthly usage tracking per plan. Incremented on each SMS send and agent run.
/// </summary>
public class UsageRecord : BaseEntity
{
    public Guid PlanId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int SmsSent { get; set; }
    public int AgentRuns { get; set; }
    public int TokensUsed { get; set; }

    public Plan Plan { get; set; } = null!;
}
