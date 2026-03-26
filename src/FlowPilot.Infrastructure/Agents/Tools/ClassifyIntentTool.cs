using System.Text.Json;
using FlowPilot.Application.Agents;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// A structured output tool that the ReplyHandlingAgent uses to return its classification result.
/// This is not an LLM call itself — it's a "sink" tool that captures the agent's decision.
/// </summary>
public sealed class ClassifyIntentTool : IAgentTool
{
    public string Name => "classify_intent";

    public string Description =>
        "Records the intent classification result for an inbound SMS. " +
        "Call this once you have determined the customer's intent and confidence level. " +
        "Valid intents: Confirm, Cancel, Reschedule, Question, Other.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "intent": {
                    "type": "string",
                    "enum": ["Confirm", "Cancel", "Reschedule", "Question", "Other"],
                    "description": "The classified intent of the customer's message"
                },
                "confidence": {
                    "type": "number",
                    "minimum": 0.0,
                    "maximum": 1.0,
                    "description": "Confidence score between 0.0 and 1.0"
                },
                "reasoning": {
                    "type": "string",
                    "description": "Brief explanation of why this intent was chosen"
                }
            },
            "required": ["intent", "confidence", "reasoning"]
        }
        """);

    public Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        // This is a "sink" tool — it just echoes back the classification for the orchestrator to capture.
        // The actual action (confirm, escalate, etc.) happens in the ReplyHandlingAgent after the loop.
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        JsonElement root = doc.RootElement;

        string intent = root.GetProperty("intent").GetString()!;
        double confidence = root.GetProperty("confidence").GetDouble();
        string reasoning = root.GetProperty("reasoning").GetString()!;

        string result = JsonSerializer.Serialize(new
        {
            recorded = true,
            intent,
            confidence,
            reasoning
        });

        return Task.FromResult(result);
    }
}
