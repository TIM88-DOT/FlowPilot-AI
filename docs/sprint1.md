# Sprint 1 — FlowPilot AI
**Goal:** Working API + React skeleton. Full AI reminder workflow. Multilingual. Demo-ready.
**Duration:** 2 weeks
**Status (2026-04-12):** Core MVP demo loop works end-to-end. Items left unchecked below are genuinely not done yet and carry into Sprint 2 (see `sprint2.md`). Canadian-market pivot: bilingual is now **FR + EN** wherever the spec says `fr + ar`.

---

## Day 1–2: Project & Domain Foundation

- [x] Create solution: `dotnet new sln -n FlowPilot`
- [x] Create projects: Api, Application, Domain, Infrastructure, Workers, Shared
- [x] Add project references (Shared → Domain → Application → Infrastructure → Api)
- [x] Add Directory.Build.props: `net8.0`, nullable, implicit usings
- [x] Define `BaseEntity`: Id (UUID), TenantId (UUID), CreatedAt, UpdatedAt, IsDeleted, DeletedAt
- [x] Create `AppDbContext` with dual EF Core global query filter (TenantId + IsDeleted)
- [x] Create `docker-compose.yml`: PostgreSQL + Seq
- [x] Migration 001: Full schema
  - [x] All entities inheriting BaseEntity
  - [x] ScheduledMessage with index on ScheduledAt
  - [x] TemplateLocaleVariant table
  - [x] Plan + UsageRecord tables
  - [x] TenantSettings with review platform fields (GooglePlaceId, FacebookPageUrl, TrustpilotUrl)
  - [x] snake_case column convention
  - [x] created_at + updated_at on all tables

## Day 3–4: Auth & Tenant

- [x] `ICurrentTenant` interface + JWT middleware implementation
- [x] `POST /api/v1/auth/register` → provision tenant + default Plan + seed system templates (fr + ar variants)  *(seeds FR + EN after Canada pivot)*
- [x] `POST /api/v1/auth/login` → accessToken in body + refreshToken in httpOnly cookie
- [x] `POST /api/v1/auth/refresh` → read httpOnly cookie, rotate + return new access token
- [x] `POST /api/v1/auth/logout` → clear cookie
- [x] Role-based auth: Owner | Manager | Staff
- [x] `IFeatureGate` service: reads Plan.FeatureFlags for current tenant
- [x] Tenant provisioning seeds fr + ar system template variants  *(FR + EN after Canada pivot)*

## Day 5–6: Customers & Consent

- [ ] Customer entity: Phone (E.164, column-encrypted), Email (column-encrypted), PreferredLanguage, Tags, NoShowScore, ConsentStatus  *(entity exists; **column encryption not implemented**)*
- [x] `ConsentRecord` entity: append-only log
- [x] `GET /api/v1/customers` — paginated, filter: search, tag, consentStatus, noShowScoreGte
- [x] `POST /api/v1/customers` — create with consent source
- [x] `GET /api/v1/customers/:id` — full profile
- [x] `PUT /api/v1/customers/:id`
- [x] `DELETE /api/v1/customers/:id` — GDPR anonymize (anonymize PII fields + soft delete)
- [x] `GET /api/v1/customers/:id/history`
- [x] `PUT /api/v1/customers/:id/consent` — creates ConsentRecord
- [x] `POST /api/v1/customers/import` — CSV → E.164 normalize → bulk insert → Pending consent

## Day 7–8: Appointments & AppointmentSync

- [x] Appointment entity with status enum: Scheduled | Confirmed | Cancelled | Missed | Completed | Rescheduled
- [x] Status transition validation (domain enforces valid transitions)
- [x] `IAppointmentSyncService.IngestFromWebhook` — idempotent on ExternalId + TenantId unique constraint
- [x] `IAppointmentSyncService.IngestFromCsv`
- [x] `GET /api/v1/appointments` — filter: status, staffId, dateRange, customerId
- [x] `POST /api/v1/appointments`
- [x] `POST /api/v1/appointments/:id/confirm`
- [ ] `POST /api/v1/appointments/:id/cancel` — triggers Service Bus SequenceNumber cancellation  *(endpoint exists; **Service Bus cancellation wiring deferred to Sprint 2**)*
- [x] `POST /api/v1/appointments/:id/complete`
- [x] `POST /api/v1/appointments/:id/reschedule`
- [x] `POST /api/webhooks/appointments/inbound` — idempotent on ExternalId + TenantId
- [ ] AuditLog entry on every status change
- [ ] `AppointmentCreated` integration event published to Service Bus  *(currently in-process MediatR; Service Bus in Sprint 2)*

## Day 9–10: Messaging & Twilio

- [x] `ISmsProvider` interface + `TwilioSmsProvider` implementation
- [ ] `IEventPublisher` interface + `AzureServiceBusPublisher` (with `PublishScheduled<T>(scheduledAt)`)  *(interface exists; **AzureServiceBusPublisher not implemented** — in-process MediatR for now)*
- [x] `TemplateLocaleVariant` rendering: locale match → tenant default → system default fallback chain
- [x] `MessagingService.Send`: consent gate → render locale variant → Twilio → increment UsageRecord
- [x] `POST /api/webhooks/sms/inbound` — validate Twilio signature, SmsSid idempotency check, enqueue event
- [x] `POST /api/webhooks/sms/status` — upsert on ProviderMessageId + Status
- [x] Inbound STOP keyword handling (STOP, UNSUBSCRIBE, CANCEL, END — case-insensitive) → opt-out synchronously before any agent  *(CANCEL intentionally removed from opt-out keywords; routed to reschedule flow instead)*
- [x] `CustomerOptedOut` domain event → cancel all pending ScheduledMessages
- [x] Template CRUD endpoints + locale variant endpoints

## Day 11–12: Reminder Scheduling with Service Bus

- [x] `IAgentTool` interface + `ToolRegistry`
- [x] Implement all 8 agent tools with JSON schemas (see architecture doc Section 7.3)  *(10 tools implemented)*
- [x] `ReminderOptimizationAgent` — get_customer_history → recommend timing → schedule_sms
- [x] `ReplyHandlingAgent` — classify intent, confidence threshold 0.85, escalate < 0.75
- [x] `ReviewRecoveryAgent` — reviewPlatformConfigured gate + 30-day cooldown (C# enforced)
- [ ] `AgentRun` + `ToolCallLog` logging on every agent execution
- [x] `ReminderSchedulerWorker` — AppointmentCreated → invoke Reminder Agent  *(triggered via event handler, not a separate worker)*
- [ ] `ReminderDispatchWorker` — Service Bus deferred delivery → consent → render → Twilio send  *(currently polling-based `ScheduledMessageDispatcher`; Service Bus deferred delivery deferred to Sprint 2)*
- [x] Store `ServiceBusSequenceNumber` on every `ScheduledMessage` row
- [ ] `AppointmentCancelled` handler — find ScheduledMessages → cancel Service Bus deferred messages via SequenceNumber  *(pending messages are cancelled in-DB; Service Bus cancellation deferred to Sprint 2)*

## Day 13–14: React Frontend Skeleton + CI/CD

- [x] `npm create vite@latest flowpilot-web -- --template react-ts`
- [x] Install: TanStack Query, React Hook Form, Zod, React Router v6, Recharts, TanStack Table, axios, shadcn/ui
- [x] `AuthProvider` — bootstrap: POST /auth/refresh before rendering protected routes
- [x] axios 401 interceptor — silent refresh + retry
- [x] Protected route guard
- [x] AppLayout: sidebar navigation
- [x] **Dashboard** — KPI cards + SMS usage meter + review platform warning card (GooglePlaceId = null)
- [x] **Customers** — table with consent badge, no-show score, tag filter
- [x] **Appointments** — list with status badges, create form
- [x] **Settings / Review** — GooglePlaceId input with preview link (`g.page/r/{id}/review`)
- [x] **Templates** — list + TemplateLocaleVariant editor (fr + ar tabs) with SMS segment counter  *(FR + EN tabs after Canada pivot)*
- [x] Integration tests:
  - [x] Tenant isolation: cross-tenant query returns 0 results
  - [x] SmsSid idempotency: second inbound with same SmsSid returns 200, no duplicate
  - [x] ExternalId idempotency: duplicate webhook → no duplicate appointment
  - [x] Soft delete filter: deleted entity not returned in list
  - [x] Consent gate: send to opted-out customer → blocked, no Twilio call
  - [x] Cancellation cascade: cancel appointment → ScheduledMessage.Status = Cancelled
- [x] GitHub Actions CI: build + test on PR
- [ ] GitHub Actions CD: deploy to Azure App Service staging on merge to main

---

## Additions that landed beyond the original spec
- Public self-service booking page (`/book/:slug`) with `PublicTenantMiddleware` + `IPublicBookingService`
- SMS Inbox page (`/sms`) with split-panel UI and realtime updates
- SignalR infrastructure: `AppointmentHub`, `SmsHub`, Postgres LISTEN/NOTIFY bridge
- `AppointmentLifecycleWorker` (auto-confirm + auto-complete)
- Instant booking-confirmation SMS + reschedule link flow
- Landing / Login / Register pages + Relora AI rebrand
- Canadian market pivot: FR + EN bilingual, Arabic/Algeria references removed

---

## Done Definition
Sprint 1 is complete when the **MVP Demo Journey** works end-to-end:
1. Register tenant → configure GooglePlaceId
2. Import customer CSV
3. Create appointment manually + via POST /webhooks/appointments/inbound
4. Reminder Agent schedules optimized SMS via Service Bus  *(currently polling; Service Bus in Sprint 2)*
5. SMS fires → customer replies "Oui" → Reply Agent confirms (0.94 confidence)
6. Staff marks Completed → Review Agent sends review SMS 2h later (French, g.page link)
7. Dashboard shows delivery rate, confirmations, token usage, agent run log
