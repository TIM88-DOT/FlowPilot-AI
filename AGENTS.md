# AGENTS.md — FlowPilot AI

AI-native communication OS for appointment-based SMBs.
Multi-tenant SaaS. Modular monolith. Event-driven. Azure-native.

---

## Solution Structure

```
FlowPilot.sln
├── src/
│   ├── FlowPilot.Api/            # Controllers, middleware, DI root
│   ├── FlowPilot.Application/    # MediatR handlers, use cases, DTOs, agent orchestration
│   ├── FlowPilot.Domain/         # BaseEntity, entities, enums, domain events
│   ├── FlowPilot.Infrastructure/ # EF Core, Twilio, Service Bus, Azure OpenAI, repos
│   ├── FlowPilot.Workers/        # IHostedService workers, Service Bus consumers
│   └── FlowPilot.Shared/         # Result<T>, Error, ICurrentTenant, IFeatureGate, guards
├── tests/
│   ├── FlowPilot.UnitTests/
│   ├── FlowPilot.IntegrationTests/  # Tenant isolation, idempotency, consent gate
│   └── FlowPilot.Architecture.Tests/ # ArchUnitNET: no cross-module references
└── infra/                        # Azure Bicep
```

## Commands

```bash
# Backend
dotnet build FlowPilot.sln
dotnet test FlowPilot.sln --no-build
dotnet run --project src/FlowPilot.Api
dotnet ef migrations add <Name> --project src/FlowPilot.Infrastructure --startup-project src/FlowPilot.Api
dotnet ef database update --project src/FlowPilot.Infrastructure --startup-project src/FlowPilot.Api

# Frontend
cd src/FlowPilot.Web
npm run dev
npm run build
npm run type-check
npm test

# Local infra
docker compose up -d   # PostgreSQL + Seq
```

---

## Critical Architecture Rules

### BaseEntity — ALL entities inherit this
```csharp
public abstract class BaseEntity {
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```
EF Core global filter on EVERY entity: `WHERE TenantId = @currentTenantId AND IsDeleted = false`
Never call `.IgnoreQueryFilters()` without a code comment explaining why.

### Multi-Tenancy — Non-negotiable rules
- TenantId ALWAYS comes from `ICurrentTenant` (resolved from JWT claims) — never from request body
- **Public endpoints exception:** `PublicTenantMiddleware` resolves TenantId from URL slug → stores in `HttpContext.Items["PublicTenantId"]` → `HttpCurrentTenant` falls back to it when no JWT. This lets all existing services work for unauthenticated booking endpoints.
- Every Service Bus message carries TenantId — workers validate it before processing
- Integration tests MUST verify cross-tenant isolation on every CI run

### Soft Delete — Never hard delete
- Set `IsDeleted = true`, `DeletedAt = UtcNow`
- Exception: GDPR anonymize = anonymize PII fields + soft delete
- BaseEntity EF filter handles exclusion automatically

### Result<T> Pattern — No exceptions for business logic
```csharp
// Always return Result<T>, never throw for business errors
public async Task<Result<AppointmentDto>> ConfirmAsync(Guid id) { ... }
// Error types live in FlowPilot.Shared
```

### AI / LLM Boundary — Critical
The application is ALWAYS the source of truth. Business rules NEVER go inside prompts.
- Consent check: deterministic C# BEFORE any LLM call
- Review 30-day cooldown: deterministic C# BEFORE LLM call
- Business hours gate: MessagingService hard check — LLM cannot override
- Status transitions: domain entities enforce valid transitions
- LLM decides: reminder timing, SMS intent classification, review request confidence

---

## Bounded Contexts (9 modules)
Tenants | Identity & Auth | Customers | Appointments | Messaging | Campaigns | AI/Agents | Billing | Analytics

**Rule:** Modules NEVER directly reference another module's DbContext.
Cross-module communication = domain events (MediatR in-process) or integration events (Azure Service Bus).
ArchUnitNET tests enforce this in CI.

---

## C# Conventions
- Minimal APIs style in `FlowPilot.Api` — controllers only if already in place
- `record` types for all DTOs
- `Result<T>` for all service method returns — no business-logic exceptions
- Async all the way — NEVER `.Result` or `.Wait()` — will cause deadlocks
- Always pass `CancellationToken` through the full call chain
- `ICurrentTenant` from DI — never resolve TenantId from request body
- EF Core: add `.AsNoTracking()` on all read-only queries
- Azure OpenAI: use `AzureOpenAIClient` from `Azure.AI.OpenAI` — never the raw OpenAI SDK
- Azure Service Bus: use `ServiceBusClient` from `Azure.Messaging.ServiceBus`
- Store `ServiceBusSequenceNumber` on every `ScheduledMessage` — needed for cancellation
- XML doc comments on all public interfaces and service methods

## React / TypeScript Conventions
- Functional components only
- TanStack Query for all server state — no manual fetch in useEffect
- React Hook Form + Zod for all forms — no uncontrolled inputs
- JWT stored in memory only — NEVER localStorage
- httpOnly cookie handles refresh token — axios interceptor handles 401 silently
- Strict TypeScript — no `any`, ever
- Tailwind + shadcn/ui only — no custom CSS unless absolutely necessary
- File naming: `PascalCase` components, `camelCase` hooks/utils

## Git
- Branches: `feat/`, `fix/`, `chore/`
- Conventional commits: `feat(messaging):`, `fix(agents):`, `chore(infra):`
- Never commit to `main` directly
- Never commit `.env`, `appsettings.*.json` with secrets, `secrets/`

---

## Key Interfaces (never redesign these without flagging)
- `ISmsProvider` — pluggable behind Twilio; test with fake
- `IAgentTool` — Name, Description, InputSchema (JSON), ExecuteAsync
- `IToolRegistry` — Register, Get, ExecuteTool (generates LLM function-calling schemas)
- `IEventPublisher` — Publish<T>, PublishScheduled<T>(scheduledAt)
- `ICurrentTenant` — TenantId, UserId, UserRole (from JWT, or `HttpContext.Items` fallback for public endpoints)
- `IPublicBookingService` — GetBusinessInfoAsync, GetAvailableSlotsAsync, BookAsync (public unauthenticated booking)
- `IFeatureGate` — IsEnabled(feature) checks Plan.FeatureFlags
- `IAppointmentSyncService` — IngestFromWebhook (idempotent), IngestFromCsv

## Public Booking Flow
- `Tenant.Slug` — unique, URL-safe, auto-generated from BusinessName on registration
- Public API: `/api/v1/public/book/{slug}` — no auth required, tenant resolved by `PublicTenantMiddleware`
- Booking creates customer (find-or-create by phone, `ConsentSource.Booking`) + appointment → triggers `AppointmentCreatedEvent` → `ReminderOptimizationAgent` auto-schedules SMS
- Slot availability algorithm: business hours − existing appointments − buffer − advance booking rules
- Frontend: `/book/:slug` — 4-step flow (service → date/time → info → confirm), code-split, mobile-first

## Idempotency Keys (memorize these)
- Inbound SMS: `InboundMessage.ProviderSmsSid` (Twilio SmsSid)
- Delivery status: `ProviderMessageId` + `Status` (upsert)
- Appointment webhook: `ExternalId` + `TenantId` unique constraint
- Service Bus consumer: `EventId` header → `ProcessedEvents` table

## What NOT to Do
- Do not add Hangfire or cron jobs — scheduler = Azure Service Bus deferred messages
- Do not implement MCP server yet — build IAgentTool + ToolRegistry first (Phase 3)
- Do not wire Stripe — Plan schema is ready, payment is deliberately last
- Do not put business rules inside prompts — C# enforces all hard gates
- Do not use `var` where the type is non-obvious
- Do not use `console.log` in React production code — use structured logging
- Do not hardcode Azure connection strings — always `IConfiguration` / env vars
- Do not add NuGet packages without checking if Azure SDK already covers it
