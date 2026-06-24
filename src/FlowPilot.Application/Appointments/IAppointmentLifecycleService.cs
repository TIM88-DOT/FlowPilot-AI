namespace FlowPilot.Application.Appointments;

/// <summary>
/// Cross-tenant appointment lifecycle scans run by background workers:
/// auto-transitioning overdue appointments and flagging at-risk (unconfirmed) ones.
/// Lives in the Application/Infrastructure layer so the logic is unit/integration testable
/// independent of the hosted worker loop.
/// </summary>
public interface IAppointmentLifecycleService
{
    /// <summary>
    /// Scans appointments past their end time + grace period and transitions them:
    /// Confirmed → Completed, Scheduled → Missed (bumping the customer's NoShowScore).
    /// Publishes AppointmentStatusChangedEvent (and AppointmentMissedEvent on no-show).
    /// Operates across all tenants.
    /// </summary>
    /// <param name="gracePeriod">How long after EndsAt to wait before transitioning.</param>
    /// <returns>The number of appointments transitioned.</returns>
    Task<int> ScanOverdueAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans Scheduled (still-unconfirmed) appointments entering the final confirmation window
    /// (StartsAt within the next <paramref name="window"/>) and flags them as at-risk by setting
    /// AtRiskAlertedAt and publishing AppointmentAtRiskEvent. Idempotent via AtRiskAlertedAt —
    /// each appointment is flagged at most once. Operates across all tenants.
    /// </summary>
    /// <param name="window">Look-ahead window before StartsAt that defines "at risk".</param>
    /// <returns>The number of appointments newly flagged as at-risk.</returns>
    Task<int> ScanAtRiskAsync(TimeSpan window, CancellationToken cancellationToken = default);
}
