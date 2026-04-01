namespace FlowPilot.Application.Messaging;

/// <summary>
/// Summary of an SMS conversation with a customer (one row per customer).
/// </summary>
public sealed record ConversationSummaryDto(
    Guid CustomerId,
    string CustomerFirstName,
    string? CustomerLastName,
    string CustomerPhone,
    string LastMessageBody,
    DateTime LastMessageAt,
    string LastMessageDirection,
    int TotalMessages);

/// <summary>
/// A single SMS message in a conversation thread.
/// </summary>
public sealed record MessageDto(
    Guid Id,
    string Body,
    string Direction,
    string Status,
    DateTime CreatedAt,
    string? FromPhone,
    string? ToPhone);

/// <summary>
/// Request body for sending a manual SMS from the inbox.
/// </summary>
public sealed record SendManualSmsRequest(string Body);
