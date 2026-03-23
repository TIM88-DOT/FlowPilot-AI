# Sprint 1 — FlowPilot AI
**Goal:** Working API + React skeleton. Full AI reminder workflow. Multilingual. Demo-ready.
**Duration:** 2 weeks

---

## Day 1–2: Project & Domain Foundation

- [ ] Create solution: `dotnet new sln -n FlowPilot`
- [ ] Create projects: Api, Application, Domain, Infrastructure, Workers, Shared
- [ ] Add project references (Shared → Domain → Application → Infrastructure → Api)
- [ ] Add Directory.Build.props: `net8.0`, nullable, implicit usings
- [ ] Define `BaseEntity`: Id (UUID), TenantId (UUID), CreatedAt, UpdatedAt, IsDeleted, DeletedAt
- [ ] Create `AppDbContext` with dual EF Core global query filter (TenantId + IsDeleted)
- [ ] Create `docker-compose.yml`: PostgreSQL + Seq
- [ ] Migration 001: Full schema
  - [ ] All entities inheriting BaseEntity
  - [ ] ScheduledMessage with index on ScheduledAt
  - [ ] TemplateLocaleVariant table
  - [ ] Plan + UsageRecord tables
  - [ ] TenantSettings with review platform fields (GooglePlaceId, FacebookPageUrl, TrustpilotUrl)
  - [ ] snake_case column convention
  - [ ] created_at + updated_at on all tables

## Day 3–4: Auth & Tenant

- [ ] `ICurrentTenant` interface + JWT middleware implementation
- [ ] `POST /api/v1/auth/register` → provision tenant + default Plan + seed system templates (fr + ar variants)
- [ ] `POST /api/v1/auth/login` → accessToken in body + refreshToken in httpOnly cookie
- [ ] `POST /api/v1/auth/refresh` → read httpOnly cookie, rotate + return new access token
- [ ] `POST /api/v1/auth/logout` → clear cookie
- [ ] Role-based auth: Owner | Manager | Staff
- [ ] `IFeatureGate` service: reads Plan.FeatureFlags for current tenant
- [ ] Tenant provisioning seeds fr + ar system template variants

## Day 5–6: Customers & Consent

- [ ] Customer entity: Phone (E.164, column-encrypted), Email (column-encrypted), PreferredLanguage, Tags, NoShowScore, ConsentStatus
- [ ] `ConsentRecord` entity: append-only log
- [ ] `GET /api/v1/customers` — paginated, filter: search, tag, consentStatus, noShowScoreGte
- [ ] `POST /api/v1/customers` — create with consent source
- [ ] `GET /api/v1/customers/:id` — full profile
- [ ] `PUT /api/v1/customers/:id`
- [ ] `DELETE /api/v1/customers/:id` — GDPR anonymize (anonymize PII fields + soft delete)
- [ ] `GET /api/v1/customers/:id/history`
- [ ] `PUT /api/v1/customers/:id/consent` — creates ConsentRecord
- [ ] `POST /api/v1/customers/import` — CSV → E.164 normalize → bulk insert → Pending consent

## Day 7–8: Appointments & AppointmentSync

- [ ] Appointment entity with status enum: Scheduled | Confirmed | Cancelled | Missed | Completed | Rescheduled
- [ ] Status transition validation (domain enforces valid transitions)
- [ ] `IAppointmentSyncService.IngestFromWebhook` — idempotent on ExternalId + TenantId unique constraint
- [ ] `IAppointmentSyncService.IngestFromCsv`
- [ ] `GET /api/v1/appointments` — filter: status, staffId, dateRange, customerId
- [ ] `POST /api/v1/appointments`
- [ ] `POST /api/v1/appointments/:id/confirm`
- [ ] `POST /api/v1/appointments/:id/cancel` — triggers Service Bus SequenceNumber cancellation
- [ ] `POST /api/v1/appointments/:id/complete`
- [ ] `POST /api/v1/appointments/:id/reschedule`
- [ ] `POST /api/webhooks/appointments/inbound` — idempotent on ExternalId + TenantId
- [ ] AuditLog entry on every status change
- [ ] `AppointmentCreated` integration event published to Service Bus

## Day 9–10: Messaging & Twilio

- [ ] `ISmsProvider` interface + `TwilioSmsProvider` implementation
- [ ] `IEventPublisher` interface + `AzureServiceBusPublisher` (with `PublishScheduled<T>(scheduledAt)`)
- [ ] `TemplateLocaleVariant` rendering: locale match → tenant default → system default fallback chain
- [ ] `MessagingService.Send`: consent gate → render locale variant → Twilio → increment UsageRecord
- [ ] `POST /api/webhooks/sms/inbound` — validate Twilio signature, SmsSid idempotency check, enqueue event
- [ ] `POST /api/webhooks/sms/status` — upsert on ProviderMessageId + Status
- [ ] Inbound STOP keyword handling (STOP, UNSUBSCRIBE, CANCEL, END — case-insensitive) → opt-out synchronously before any agent
- [ ] `CustomerOptedOut` domain event → cancel all pending ScheduledMessages
- [ ] Template CRUD endpoints + locale variant endpoints

## Day 11–12: Reminder Scheduling with Service Bus

- [ ] `IAgentTool` interface + `ToolRegistry`
- [ ] Implement all 8 agent tools with JSON schemas (see architecture doc Section 7.3)
- [ ] `ReminderOptimizationAgent` — get_customer_history → recommend timing → schedule_sms
- [ ] `ReplyHandlingAgent` — classify intent, confidence threshold 0.85, escalate < 0.75
- [ ] `ReviewRecoveryAgent` — reviewPlatformConfigured gate + 30-day cooldown (C# enforced)
- [ ] `AgentRun` + `ToolCallLog` logging on every agent execution
- [ ] `ReminderSchedulerWorker` — AppointmentCreated → invoke Reminder Agent
- [ ] `ReminderDispatchWorker` — Service Bus deferred delivery → consent → render → Twilio send
- [ ] Store `ServiceBusSequenceNumber` on every `ScheduledMessage` row
- [ ] `AppointmentCancelled` handler — find ScheduledMessages → cancel Service Bus deferred messages via SequenceNumber

## Day 13–14: React Frontend Skeleton + CI/CD

- [ ] `npm create vite@latest flowpilot-web -- --template react-ts`
- [ ] Install: TanStack Query, React Hook Form, Zod, React Router v6, Recharts, TanStack Table, axios, shadcn/ui
- [ ] `AuthProvider` — bootstrap: POST /auth/refresh before rendering protected routes
- [ ] axios 401 interceptor — silent refresh + retry
- [ ] Protected route guard
- [ ] AppLayout: sidebar navigation
- [ ] **Dashboard** — KPI cards + SMS usage meter + review platform warning card (GooglePlaceId = null)
- [ ] **Customers** — table with consent badge, no-show score, tag filter
- [ ] **Appointments** — list with status badges, create form
- [ ] **Settings / Review** — GooglePlaceId input with preview link (`g.page/r/{id}/review`)
- [ ] **Templates** — list + TemplateLocaleVariant editor (fr + ar tabs) with SMS segment counter
- [ ] Integration tests:
  - [ ] Tenant isolation: cross-tenant query returns 0 results
  - [ ] SmsSid idempotency: second inbound with same SmsSid returns 200, no duplicate
  - [ ] ExternalId idempotency: duplicate webhook → no duplicate appointment
  - [ ] Soft delete filter: deleted entity not returned in list
  - [ ] Consent gate: send to opted-out customer → blocked, no Twilio call
  - [ ] Cancellation cascade: cancel appointment → ScheduledMessage.Status = Cancelled
- [ ] GitHub Actions CI: build + test on PR
- [ ] GitHub Actions CD: deploy to Azure App Service staging on merge to main

---

## Done Definition
Sprint 1 is complete when the **MVP Demo Journey** works end-to-end:
1. Register tenant → configure GooglePlaceId
2. Import customer CSV
3. Create appointment manually + via POST /webhooks/appointments/inbound
4. Reminder Agent schedules optimized SMS via Service Bus
5. SMS fires → customer replies "Oui" → Reply Agent confirms (0.94 confidence)
6. Staff marks Completed → Review Agent sends review SMS 2h later (French, g.page link)
7. Dashboard shows delivery rate, confirmations, token usage, agent run log
