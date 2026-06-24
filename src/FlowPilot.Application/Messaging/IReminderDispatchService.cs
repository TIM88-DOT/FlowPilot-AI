namespace FlowPilot.Application.Messaging;

/// <summary>
/// Dispatches due scheduled reminder messages across all tenants. Extracted from the
/// ScheduledMessageDispatcher worker so the consent gate, the appointment-no-longer-Scheduled
/// cancellation, and the send/usage logic are testable independent of the hosted polling loop.
/// </summary>
public interface IReminderDispatchService
{
    /// <summary>
    /// Finds all Pending scheduled messages whose ScheduledAt is due and dispatches each:
    /// cancels if the customer opted out or the linked appointment is no longer Scheduled,
    /// fails on empty body, otherwise sends via ISmsProvider, logs an outbound Message and
    /// increments the tenant's SMS usage. Operates across all tenants.
    /// </summary>
    /// <returns>The number of messages processed (sent, cancelled, or failed).</returns>
    Task<int> DispatchDueAsync(CancellationToken cancellationToken = default);
}
