using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Application.Messaging;
using FlowPilot.Shared;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Sends an SMS immediately via the messaging pipeline (consent gate → provider → log).
/// </summary>
public sealed class SendSmsTool : IAgentTool
{
    private readonly IMessagingService _messagingService;

    public SendSmsTool(IMessagingService messagingService) => _messagingService = messagingService;

    public string Name => "send_sms";

    public string Description =>
        "Sends an SMS immediately to a customer. Goes through the full consent gate and provider pipeline. " +
        "Use schedule_sms for future delivery instead.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "customerId": { "type": "string", "format": "uuid", "description": "The customer to send to" },
                "body": { "type": "string", "description": "The SMS text to send" }
            },
            "required": ["customerId", "body"]
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        JsonElement root = doc.RootElement;

        Guid customerId = Guid.Parse(root.GetProperty("customerId").GetString()!);
        string body = root.GetProperty("body").GetString()!;

        var request = new SendRawSmsRequest(customerId, body);
        Result<SendSmsResponse> result = await _messagingService.SendRawAsync(request, cancellationToken);

        if (result.IsFailure)
            return JsonSerializer.Serialize(new { success = false, error = result.Error.Description });

        return JsonSerializer.Serialize(new
        {
            success = true,
            messageId = result.Value.MessageId,
            providerMessageId = result.Value.ProviderMessageId,
            segmentCount = result.Value.SegmentCount
        });
    }
}
