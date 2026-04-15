using FlowPilot.Shared;

namespace FlowPilot.Application.Stats;

/// <summary>
/// Computes real-time dashboard KPIs for the current tenant.
/// </summary>
public interface IDashboardStatsService
{
    /// <summary>
    /// Returns no-show rate (last 30 days) and review SMS count (current month).
    /// </summary>
    Task<Result<DashboardStatsDto>> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Dashboard KPI data.
/// </summary>
public sealed record DashboardStatsDto(
    decimal NoShowRatePercent,
    int TotalAppointmentsLast30Days,
    int MissedAppointmentsLast30Days,
    int ReviewsSentThisMonth,
    int SmsSentThisMonth,
    int AtRiskCount);
