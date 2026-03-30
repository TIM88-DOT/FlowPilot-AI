using FlowPilot.Application.Agents;

namespace FlowPilot.Workers;

/// <summary>
/// No-op agent orchestrator for the Workers host.
/// AI agents require Azure OpenAI which is only configured in the API host.
/// Agent event handlers still fire (MediatR assembly scan) but gracefully skip execution.
/// </summary>
internal sealed class NoOpAgentOrchestrator : IAgentOrchestrator
{
    /// <inheritdoc />
    public Task<AgentRunResult> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentRunResult(
            AgentRunId: Guid.Empty,
            FinalResponse: null,
            ToolCallCount: 0,
            TokensUsed: 0,
            DurationMs: 0,
            Success: false,
            ErrorMessage: "Agent orchestration is not available in the Workers host."));
    }
}
