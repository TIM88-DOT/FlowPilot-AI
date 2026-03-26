namespace FlowPilot.Application.Agents;

/// <summary>
/// Orchestrates an AI agent execution: sends system prompt + user message to Azure OpenAI,
/// executes tool calls in a loop, and logs everything to AgentRun + ToolCallLog.
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Runs an agent with the given configuration and returns the result.
    /// </summary>
    Task<AgentRunResult> RunAsync(AgentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for a single agent execution.
/// </summary>
/// <param name="AgentType">Agent identifier (e.g. "ReminderOptimization").</param>
/// <param name="SystemPrompt">The system prompt defining the agent's role and behavior.</param>
/// <param name="UserMessage">The user message triggering the agent (e.g. "New appointment created...").</param>
/// <param name="ToolNames">Which tools from the registry this agent is allowed to use.</param>
/// <param name="AppointmentId">Optional appointment context for logging.</param>
/// <param name="CustomerId">Optional customer context for logging.</param>
/// <param name="TriggerEvent">What triggered this agent run (e.g. "AppointmentCreated").</param>
public sealed record AgentRequest(
    string AgentType,
    string SystemPrompt,
    string UserMessage,
    IReadOnlyList<string> ToolNames,
    Guid? AppointmentId = null,
    Guid? CustomerId = null,
    string? TriggerEvent = null);

/// <summary>
/// Result of an agent execution.
/// </summary>
/// <param name="AgentRunId">The persisted AgentRun record ID.</param>
/// <param name="FinalResponse">The AI's final text response after all tool calls.</param>
/// <param name="ToolCallCount">Total number of tool calls made.</param>
/// <param name="TokensUsed">Total tokens consumed.</param>
/// <param name="DurationMs">Total execution time in milliseconds.</param>
/// <param name="Success">Whether the agent completed without errors.</param>
/// <param name="ErrorMessage">Error message if the agent failed.</param>
public sealed record AgentRunResult(
    Guid AgentRunId,
    string? FinalResponse,
    int ToolCallCount,
    int TokensUsed,
    int DurationMs,
    bool Success,
    string? ErrorMessage = null);
