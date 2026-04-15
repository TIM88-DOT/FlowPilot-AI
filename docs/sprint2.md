# Sprint 2 — Path to Production
**Goal:** Ship FlowPilot / Relora AI to a real Azure environment with real Twilio senders, real money, and enough observability to run it safely with a small pilot.
**Duration:** 2 weeks
**Prereq:** Sprint 1 shipped. MVP demo journey works locally end-to-end.

---

## Workstream A — Azure Infrastructure (Bicep + CD)

- [ ] Author `infra/main.bicep`: Resource Group, App Service Plan, App Service (API), App Service (Workers), Postgres Flexible Server, Service Bus namespace + queues, Key Vault, Log Analytics + App Insights, Storage account
- [ ] Parameterize per environment: `infra/env/dev.bicepparam`, `infra/env/prod.bicepparam`
- [ ] Managed Identity on both App Services; grant Key Vault secrets read + Service Bus send/listen
- [ ] GitHub Actions CD workflow: on push to `main`, build → test → `az deployment group create` → deploy API + Workers → run EF migrations via one-off job
- [ ] Front Door or App Service custom domain + managed cert for `app.reloraai.com` + `api.reloraai.com`
- [ ] Health checks and readiness probes wired to `/health`

## Workstream B — Azure Service Bus (deferred messages for real)

- [ ] Implement `AzureServiceBusPublisher : IEventPublisher` with `PublishScheduled<T>(scheduledAt)` using Service Bus scheduled delivery
- [ ] `ScheduledMessageDispatcher` → swap polling for Service Bus consumer (`ServiceBusProcessor`) reading from `reminders` queue
- [ ] Store `ServiceBusSequenceNumber` on enqueue; cancel via `CancelScheduledMessageAsync` on appointment cancel/reschedule
- [ ] Dead-letter queue handler: surface poison messages to an internal admin view
- [ ] `ProcessedEvents` table for worker idempotency (EventId header → row upsert)
- [ ] Integration test: schedule → cancel → verify Service Bus message is gone

## Workstream C — Billing (Stripe)

- [ ] `StripeCustomerService` — create Stripe customer on tenant register, store `StripeCustomerId` on Tenant
- [ ] `POST /api/v1/billing/checkout` → Stripe Checkout session for Plan upgrade
- [ ] `POST /api/webhooks/stripe` — verify signature, handle `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`, `invoice.payment_failed`
- [ ] Tenant → Plan linkage updates on subscription events
- [ ] Usage-based billing: at month close, report `UsageRecord` to Stripe metered subscription item (SMS overage)
- [ ] Billing page in app: current plan, SMS usage meter, invoice history, upgrade button
- [ ] Feature-gate enforcement tested end-to-end: downgrade → premium feature disabled

## Workstream D — Twilio at launch (Model A: global sender)

- [ ] Toll-free or short-code verification submitted for Canada (A2P)
- [ ] Per-tenant `TenantSettings.SmsSenderId` so outbound `From` routes correctly even on shared infra
- [ ] Brand/campaign metadata surfaced in Settings page so operators can see verification state
- [ ] Document (in `docs/twilio-onboarding.md`) the migration path to **Model B** (per-tenant subaccounts) for post-launch scale
- [ ] Outbound rate-limit + circuit breaker around `TwilioSmsProvider` (protect against API outages)

## Workstream E — Observability & operations

- [ ] Serilog → App Insights sink (structured logs with TenantId enriched on every scope)
- [ ] App Insights dashboards: SMS send volume, delivery rate, agent run latency, error rate per endpoint
- [ ] Alert rules: delivery rate < 90% for 15min, Service Bus dead-letter > 0, Postgres connection saturation, unhandled exceptions > 5/min
- [ ] Audit log viewer in the app (read-only table of `AuditLog` with filters)
- [ ] `/admin` internal route (Owner role only): tenant list, feature-flag overrides, kill switch per tenant

## Workstream F — Pilot-blocker polish

- [ ] Password reset flow (`POST /auth/forgot-password` → email with one-time token → reset form)
- [ ] Email verification on register (Postmark or Azure Communication Services)
- [ ] Legal pages: Terms of Service, Privacy Policy, SMS compliance disclosure (linked from booking page + register form)
- [ ] PIPEDA compliance checklist for Canadian market: data residency in `canadacentral`, DSAR export endpoint, consent audit trail already in place ✓
- [ ] Onboarding wizard: after register, walk owner through (1) configure business hours, (2) add first service, (3) upload customer CSV, (4) test-send to own phone
- [ ] Empty-state illustrations and loading skeletons on every list view

## Workstream G — Pre-appointment escalation (at-risk confirmation)

Industry-standard two-touch reminder flow + staff-facing at-risk alert. Closes the gap where a customer never confirms and the business only notices at the appointment time.

- [x] Reminder agent schedules **two** reminders per appointment: T−24h (friendly) and T−3h (urgent "last chance"). Skip-logic for same-day bookings.
- [x] `ScheduledMessageDispatcher` loads the related appointment and **cancels** the second reminder if the appointment is no longer `Scheduled` (customer already confirmed / cancelled / rescheduled).
- [x] `Appointment.AtRiskAlertedAt` column as an idempotency guard.
- [x] `AppointmentLifecycleWorker` pre-appointment scan: for `Scheduled && StartsAt ∈ (now, now + AtRiskWindow] && AtRiskAlertedAt == null`, set `AtRiskAlertedAt = now` and publish `AppointmentAtRiskEvent`. Window configurable via `Appointments:AtRiskWindowHours` (default 3h).
- [x] `AppointmentRealtimeBridge` fans `AppointmentAtRiskEvent` out to the `appointments` SignalR hub as `"AppointmentAtRisk"`.
- [x] `DashboardStatsService` returns an `AtRiskCount` KPI (upcoming Scheduled with `AtRiskAlertedAt != null`).
- [x] Web dashboard: red "At-risk" KPI card + red-dot badge on each at-risk appointment row. `useAppointmentEvents` invalidates queries on `AppointmentAtRisk` push.
- [ ] EF migration `AddAppointmentAtRiskAlert` — add nullable `at_risk_alerted_at` column to `appointments`. **Action required:** stop services, then `dotnet ef migrations add AddAppointmentAtRiskAlert --project src/FlowPilot.Infrastructure --startup-project src/FlowPilot.Api && dotnet ef database update`.
- [ ] Integration test: create same-day appointment → time-skip inside window → assert `AtRiskAlertedAt` set exactly once, event published exactly once.
- [ ] Integration test: first reminder fires → customer confirms → second reminder dispatcher run marks the T−3h message as `Cancelled` instead of sending.

---

## Out of Scope — deliberately deferred

Tracked in `todo.txt` and for **Sprint 3**:
- **WhatsApp** integration (Twilio Conversations API)
- **Staff management** + customer-chooses-staff booking flow
- **MCP server** exposing `IAgentTool` registry to external LLM clients
- **Twilio Model B** — per-tenant subaccount provisioning
- Calendar sync (Google Calendar / Outlook) beyond webhook ingest
- Native mobile app

---

## Done Definition
Sprint 2 is complete when:
1. A fresh tenant can register at `app.reloraai.com`, pay with a real Stripe card, and receive a real SMS reminder on a verified Canadian phone number — all from Azure, no localhost.
2. Service Bus schedules and cancels reminders; dead letters are visible.
3. App Insights shows delivery rate + agent latency dashboards with alerting.
4. CI deploys API + Workers + migrations on every merge to `main` without manual steps.
5. Pilot customer #1 can be onboarded end-to-end in under 15 minutes.
