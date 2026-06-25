using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Concurrency-safe SMS usage metering.
/// <para>
/// The same tenant's SMS can be sent in parallel from several paths — the request thread
/// (<see cref="MessagingService"/>), the reminder dispatcher (<see cref="ReminderDispatchService"/>)
/// and the no-show worker (<see cref="NoShowFollowUpSmsHandler"/>) — all targeting the single
/// <c>(plan, year, month)</c> <see cref="UsageRecord"/>. A read-then-insert find-or-create races on
/// the unique <c>ix_usage_records_plan_id_year_month</c> and throws Postgres 23505.
/// This performs an atomic <c>INSERT … ON CONFLICT DO UPDATE</c> so concurrent sends serialize in
/// the database instead of colliding.
/// </para>
/// </summary>
public static class UsageTracker
{
    /// <summary>
    /// Atomically increments this month's <c>SmsSent</c> counter for the tenant's plan, creating the
    /// monthly <see cref="UsageRecord"/> on first use. No-op if the tenant has no plan. Runs as its own
    /// statement (does not flush the caller's tracked changes), so call it alongside the message save.
    /// </summary>
    public static async Task IncrementSmsSentAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        Plan? plan = await db.Plans
            .IgnoreQueryFilters() // Callers include cross-tenant workers with no ambient tenant
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && !p.IsDeleted, cancellationToken);

        if (plan is null)
            return;

        DateTime now = DateTime.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO usage_records
    (id, tenant_id, plan_id, year, month, sms_sent, agent_runs, tokens_used, created_at, updated_at, is_deleted)
VALUES
    ({Guid.NewGuid()}, {tenantId}, {plan.Id}, {now.Year}, {now.Month}, 1, 0, 0, {now}, {now}, false)
ON CONFLICT (plan_id, year, month)
DO UPDATE SET sms_sent = usage_records.sms_sent + 1, updated_at = {now}", cancellationToken);
    }
}
