using FlowPilot.Application.Customers;
using FlowPilot.Shared;

namespace FlowPilot.Application.Messaging;

/// <summary>
/// Request to send an SMS to a customer. The service handles consent gating,
/// template rendering, provider dispatch, and usage tracking.
/// </summary>
public sealed record SendSmsRequest(
    Guid CustomerId,
    Guid TemplateId,
    Dictionary<string, string> Variables);

/// <summary>
/// Request to send a raw (non-templated) SMS to a customer.
/// </summary>
public sealed record SendRawSmsRequest(
    Guid CustomerId,
    string Body);

/// <summary>
/// Result of a successful SMS send.
/// </summary>
public sealed record SendSmsResponse(
    Guid MessageId,
    string? ProviderMessageId,
    string RenderedBody,
    int? SegmentCount);

/// <summary>
/// Orchestrates SMS sending: consent gate → render template → send via provider → log message → update usage.
/// </summary>
public interface IMessagingService
{
    /// <summary>
    /// Sends a templated SMS. Fails if customer is not OptedIn.
    /// </summary>
    Task<Result<SendSmsResponse>> SendTemplatedAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a raw SMS body. Fails if customer is not OptedIn.
    /// </summary>
    Task<Result<SendSmsResponse>> SendRawAsync(SendRawSmsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an inbound SMS. Handles STOP keyword opt-out before anything else.
    /// Idempotent on ProviderSmsSid.
    /// </summary>
    Task<Result> ProcessInboundAsync(InboundSmsWebhook webhook, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a delivery status update from the SMS provider.
    /// Idempotent upsert on ProviderMessageId + Status.
    /// </summary>
    Task<Result> ProcessDeliveryStatusAsync(DeliveryStatusWebhook webhook, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns paginated conversation summaries (one per customer with messages), ordered by most recent.
    /// </summary>
    Task<Result<PagedResult<ConversationSummaryDto>>> GetConversationsAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns paginated messages for a specific customer conversation, newest first.
    /// </summary>
    Task<Result<PagedResult<MessageDto>>> GetMessagesAsync(
        Guid customerId, int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// Inbound SMS webhook payload (e.g., from Twilio).
/// </summary>
public sealed record InboundSmsWebhook(
    string ProviderSmsSid,
    string FromPhone,
    string ToPhone,
    string Body);

/// <summary>
/// Delivery status webhook payload (e.g., from Twilio).
/// </summary>
public sealed record DeliveryStatusWebhook(
    string ProviderMessageId,
    string Status);
