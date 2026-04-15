using FlowPilot.Application.Stats;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Stats;

/// <summary>
/// Computes dashboard KPIs from appointment and messaging data.
/// </summary>
public sealed class DashboardStatsService : IDashboardStatsService
{
    private readonly AppDbContext _db;

    public DashboardStatsService(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<Result<DashboardStatsDto>> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        DateTime utcNow = DateTime.UtcNow;
        DateTime thirtyDaysAgo = utcNow.AddDays(-30);
        int currentYear = utcNow.Year;
        int currentMonth = utcNow.Month;

        // No-show rate: Missed / (Completed + Missed + Cancelled) over last 30 days
        // Only count terminal appointment states to get a meaningful rate
        var appointmentCounts = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.StartsAt >= thirtyDaysAgo && a.StartsAt <= utcNow)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Missed = g.Count(a => a.Status == AppointmentStatus.Missed)
            })
            .FirstOrDefaultAsync(cancellationToken);

        int totalAppointments = appointmentCounts?.Total ?? 0;
        int missedAppointments = appointmentCounts?.Missed ?? 0;
        decimal noShowRate = totalAppointments > 0
            ? Math.Round((decimal)missedAppointments / totalAppointments * 100, 1)
            : 0;

        // Review SMS sent this month: count outbound messages that contain review-related content
        // We track via AgentRun with type "ReviewRecovery" that completed successfully
        int reviewsSent = await _db.AgentRuns
            .AsNoTracking()
            .Where(r => r.AgentType == "ReviewRecovery"
                        && r.Status == "Completed"
                        && r.StartedAt.Year == currentYear
                        && r.StartedAt.Month == currentMonth)
            .CountAsync(cancellationToken);

        // Total SMS sent this month from UsageRecord
        int smsSent = await _db.UsageRecords
            .AsNoTracking()
            .Where(u => u.Year == currentYear && u.Month == currentMonth)
            .Select(u => u.SmsSent)
            .FirstOrDefaultAsync(cancellationToken);

        // At-risk: Scheduled appointments the lifecycle worker flagged as still unconfirmed.
        // We do NOT filter on StartsAt > now — once the appointment passes its start time it
        // is still "at risk" until the lifecycle worker transitions it to Missed (after EndsAt + grace).
        // Filtering on StartsAt would make the KPI briefly drop to 0 between StartsAt and the Missed transition.
        int atRiskCount = await _db.Appointments
            .AsNoTracking()
            .CountAsync(a => a.Status == AppointmentStatus.Scheduled
                             && a.AtRiskAlertedAt != null, cancellationToken);

        return Result.Success(new DashboardStatsDto(
            NoShowRatePercent: noShowRate,
            TotalAppointmentsLast30Days: totalAppointments,
            MissedAppointmentsLast30Days: missedAppointments,
            ReviewsSentThisMonth: reviewsSent,
            SmsSentThisMonth: smsSent,
            AtRiskCount: atRiskCount));
    }
}
