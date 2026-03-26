using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using Azure.AI.OpenAI;
using FlowPilot.Application.Agents;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace FlowPilot.Infrastructure.Agents;

/// <summary>
/// Core agent orchestrator. Sends messages to Azure OpenAI, executes tool calls in a loop,
/// and persists AgentRun + ToolCallLog records for full observability.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly ChatClient? _chatClient;
    private readonly IToolRegistry _toolRegistry;
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IServiceProvider serviceProvider,
        IToolRegistry toolRegistry,
        AppDbContext db,
        ICurrentTenant currentTenant,
        IOptions<AzureOpenAISettings> settings,
        ILogger<AgentOrchestrator> logger)
    {
        // ChatClient is optional — null when Azure OpenAI is not configured (dev/test)
        _chatClient = serviceProvider.GetService(typeof(ChatClient)) as ChatClient;
        _toolRegistry = toolRegistry;
        _db = db;
        _currentTenant = currentTenant;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AgentRunResult> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            _logger.LogWarning("Agent {AgentType} skipped — Azure OpenAI is not configured", request.AgentType);
            return new AgentRunResult(Guid.Empty, null, 0, 0, 0, false,
                "Azure OpenAI is not configured. Set AzureOpenAI:Endpoint and AzureOpenAI:ApiKey.");
        }

        var stopwatch = Stopwatch.StartNew();
        var agentRun = new AgentRun
        {
            Id = Guid.NewGuid(),
            TenantId = _currentTenant.TenantId,
            AgentType = request.AgentType,
            AppointmentId = request.AppointmentId,
            CustomerId = request.CustomerId,
            TriggerEvent = request.TriggerEvent,
            Status = "Running",
            StartedAt = DateTime.UtcNow
        };

        _db.AgentRuns.Add(agentRun);
        int totalTokens = 0;
        int toolCallCount = 0;
        string? finalResponse = null;

        try
        {
            // Build the message list
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(request.SystemPrompt),
                new UserChatMessage(request.UserMessage)
            };

            // Get the tools this agent is allowed to use
            IReadOnlyList<IAgentTool> agentTools = _toolRegistry.GetTools(request.ToolNames);
            ChatCompletionOptions options = BuildOptions(agentTools);

            // The core loop — call Azure OpenAI, execute tools, repeat
            int iteration = 0;
            while (iteration < _settings.MaxToolCallIterations)
            {
                iteration++;

                ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
                totalTokens += (completion.Usage?.TotalTokenCount ?? 0);

                // If the AI is done (no more tool calls), capture final response and exit
                if (completion.FinishReason != ChatFinishReason.ToolCalls)
                {
                    finalResponse = completion.Content.Count > 0
                        ? completion.Content[0].Text
                        : null;
                    break;
                }

                // Add the assistant's tool-call message to history
                messages.Add(new AssistantChatMessage(completion));

                // Execute each tool call the AI requested
                foreach (ChatToolCall toolCall in completion.ToolCalls)
                {
                    var toolStopwatch = Stopwatch.StartNew();
                    string toolResult;
                    bool succeeded;
                    string? errorMessage = null;

                    try
                    {
                        toolResult = await _toolRegistry.ExecuteToolAsync(
                            toolCall.FunctionName,
                            toolCall.FunctionArguments.ToString(),
                            cancellationToken);
                        succeeded = true;
                    }
                    catch (Exception ex)
                    {
                        toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                        succeeded = false;
                        errorMessage = ex.Message;
                        _logger.LogWarning(ex, "Tool {ToolName} failed during agent run {AgentRunId}",
                            toolCall.FunctionName, agentRun.Id);
                    }

                    toolStopwatch.Stop();
                    toolCallCount++;

                    // Log the tool call
                    _db.ToolCallLogs.Add(new ToolCallLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = _currentTenant.TenantId,
                        AgentRunId = agentRun.Id,
                        ToolName = toolCall.FunctionName,
                        InputJson = toolCall.FunctionArguments.ToString(),
                        OutputJson = toolResult,
                        DurationMs = (int)toolStopwatch.ElapsedMilliseconds,
                        Succeeded = succeeded,
                        ErrorMessage = errorMessage
                    });

                    // Send the tool result back to the AI
                    messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
                }
            }

            // Mark as completed
            stopwatch.Stop();
            agentRun.Status = "Completed";
            agentRun.CompletedAt = DateTime.UtcNow;
            agentRun.TokensUsed = totalTokens;
            agentRun.DurationMs = (int)stopwatch.ElapsedMilliseconds;

            await _db.SaveChangesAsync(cancellationToken);

            // Update usage record
            await IncrementAgentUsageAsync(totalTokens, cancellationToken);

            _logger.LogInformation(
                "Agent {AgentType} completed in {DurationMs}ms — {ToolCallCount} tool calls, {TokensUsed} tokens",
                request.AgentType, agentRun.DurationMs, toolCallCount, totalTokens);

            return new AgentRunResult(
                agentRun.Id, finalResponse, toolCallCount, totalTokens,
                (int)stopwatch.ElapsedMilliseconds, true);
        }
        catch (ClientResultException crEx)
        {
            stopwatch.Stop();

            // Extract the full error response from Azure OpenAI
            string fullError = crEx.Message;
            try
            {
                if (crEx.GetRawResponse() is { } rawResponse)
                {
                    string responseBody = rawResponse.Content.ToString();
                    fullError = $"{crEx.Message} | Status: {crEx.Status} | Response: {responseBody}";
                }
            }
            catch { /* ignore extraction errors */ }

            agentRun.Status = "Failed";
            agentRun.ErrorMessage = fullError;
            agentRun.CompletedAt = DateTime.UtcNow;
            agentRun.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            agentRun.TokensUsed = totalTokens;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogError(crEx, "Agent {AgentType} failed after {DurationMs}ms — Azure OpenAI error: {FullError}",
                request.AgentType, agentRun.DurationMs, fullError);

            return new AgentRunResult(
                agentRun.Id, null, toolCallCount, totalTokens,
                (int)stopwatch.ElapsedMilliseconds, false, fullError);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            agentRun.Status = "Failed";
            agentRun.ErrorMessage = ex.Message;
            agentRun.CompletedAt = DateTime.UtcNow;
            agentRun.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            agentRun.TokensUsed = totalTokens;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogError(ex, "Agent {AgentType} failed after {DurationMs}ms", request.AgentType, agentRun.DurationMs);

            return new AgentRunResult(
                agentRun.Id, null, toolCallCount, totalTokens,
                (int)stopwatch.ElapsedMilliseconds, false, ex.Message);
        }
    }

    /// <summary>
    /// Builds ChatCompletionOptions with the tool definitions for the given agent tools.
    /// </summary>
    private static ChatCompletionOptions BuildOptions(IReadOnlyList<IAgentTool> tools)
    {
        var options = new ChatCompletionOptions();

        foreach (IAgentTool tool in tools)
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                tool.InputSchema));
        }

        return options;
    }

    /// <summary>
    /// Increments the AgentRuns and TokensUsed counters on the current month's UsageRecord.
    /// </summary>
    private async Task IncrementAgentUsageAsync(int tokensUsed, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        Plan? plan = await _db.Plans
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
            return;

        UsageRecord? usage = await _db.UsageRecords
            .FirstOrDefaultAsync(u => u.PlanId == plan.Id && u.Year == now.Year && u.Month == now.Month, cancellationToken);

        if (usage is null)
        {
            usage = new UsageRecord
            {
                PlanId = plan.Id,
                Year = now.Year,
                Month = now.Month,
                AgentRuns = 1,
                TokensUsed = tokensUsed
            };
            _db.UsageRecords.Add(usage);
        }
        else
        {
            usage.AgentRuns++;
            usage.TokensUsed += tokensUsed;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
