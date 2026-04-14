# Relora AI ✨

**Appointments that manage themselves.**

An AI-native communication OS for appointment-based small businesses — salons, clinics, barbers, studios, dentists. Bilingual (FR + EN), built for the Canadian market.

> **Heads up — the product was renamed.** The repo, .NET solution, projects, and namespaces are all still `FlowPilot.*` (the old working name). Everything the user sees is **Relora AI**. The source-tree rename is planned but not yet done, so expect `FlowPilot` in paths and `dotnet` commands.

---

## 💡 Why this exists

Small businesses don't lose money because they're bad at their craft. They lose it in the gaps between appointments — no-shows, silent customers, reviews that never got asked for, reminders sent in the wrong language.

The owner of a 3-chair salon doesn't want a CRM or another dashboard. They want the *outcome*: full chairs, clients on time, 5-star reviews rolling in. Everything else is overhead.

Existing tools make them work harder. Booking software sends dumb reminders at fixed hours. Marketing tools spam everyone with the same campaign. Review platforms ask for feedback at random. And almost all of them are English-only — a non-starter in Montreal, Québec City, or half of Ontario.

Relora flips this. The AI *is* the workflow, not a chatbot bolted onto one. It picks when to remind, in which language, using which tone. It reads incoming replies and updates the appointment with no human in the loop. It waits for the right moment to ask for a review, and only asks clients likely to leave a good one.

**The bet:** the next generation of SMB software won't have settings pages. It will have outcomes, and an AI agent that takes responsibility for them.

---

## 🚀 What it does

- **Smart bilingual reminders.** Per-client language (FR/EN), per-client send-time picked by an LLM agent based on past response patterns — not a static 24h-before rule.
- **Conversational SMS, both directions.** Inbound replies are intent-classified, matched to the appointment, and answered or rescheduled automatically. A reschedule link drops the client on a mobile public booking page.
- **Automatic review recovery.** Post-appointment, a deterministic cooldown gate + AI confidence score decide who gets a review request. Link goes straight to Google, Facebook, or Trustpilot.
- **Public booking without an account.** Every tenant gets `/book/{slug}`. Pick service → slot → phone → confirm. Consent capture and phone-based dedup are handled server-side.
- **No-show scoring + at-risk flags.** Rolling per-customer score, extra confirmation touch for at-risk appointments, auto-completion when end time passes.
- **Owner-grade dashboard.** Live feed over SignalR, weekly stats, at-risk list — glanceable on a phone between clients.

---

## 🧭 Design principles

- **The application is the source of truth.** Business rules never live in a prompt. Consent checks, cooldown windows, business-hours gates, status transitions — deterministic C# that runs *before* any LLM call. The AI decides timing, tone, and intent; the code enforces what's legal and possible.
- **Multi-tenant isolation is a precondition, not a feature.** `TenantId` on every entity, EF Core global query filters on every query, tenant validation on every Service Bus message, cross-tenant tests on every CI run. A tenant leak is product-ending, so it's built to be impossible.
- **Privacy-first.** Soft delete everywhere, GDPR-style anonymization on customer delete, append-only consent log. Column-level encryption on phone/email is in progress.
- **Events, not cron jobs.** No Hangfire, no Quartz. Scheduling = Azure Service Bus deferred messages with sequence numbers stored on the row, so anything can be cancelled deterministically on reschedule or cancel.
- **One monolith, nine bounded contexts.** Tenants · Identity & Auth · Customers · Appointments · Messaging · Campaigns · AI/Agents · Billing · Analytics. Modules never touch each other's `DbContext` — ArchUnitNET tests fail the build if they do.

---

## 🛠️ Tech

**Backend**
- .NET 8 minimal APIs, MediatR for in-process domain events, `Result<T>` on all service boundaries
- EF Core 8 on PostgreSQL, snake_case, global query filters on every entity
- Azure Service Bus for scheduled messages, integration events, and worker queues
- Azure OpenAI (`Azure.AI.OpenAI`) for reminder optimization, intent classification, review confidence
- Twilio SMS behind `ISmsProvider` (fake provider for tests and local dev)
- SignalR for real-time dashboard, Seq for local structured logs

**Frontend**
- React 18 + TypeScript (strict, no `any`)
- TanStack Query for server state, React Hook Form + Zod for forms
- Tailwind + shadcn/ui, Mintlify-inspired design system (see [`DESIGN.md`](DESIGN.md))
- JWT in memory, refresh token in `httpOnly` cookie, code-split public booking flow

**Infra**
- Azure Bicep in `infra/`, Docker Compose for local PostgreSQL + Seq

---

## 📁 Repository layout

```
FlowPilot.sln
├── src/
│   ├── FlowPilot.Api/            Controllers, middleware, DI root
│   ├── FlowPilot.Application/    MediatR handlers, DTOs, agent orchestration
│   ├── FlowPilot.Domain/         Entities, enums, domain events
│   ├── FlowPilot.Infrastructure/ EF Core, Twilio, Service Bus, Azure OpenAI
│   ├── FlowPilot.Workers/        IHostedService workers, Service Bus consumers
│   ├── FlowPilot.Shared/         Result<T>, Error types, guards, interfaces
│   └── FlowPilot.Web/            React + Vite frontend
├── tests/
│   ├── FlowPilot.UnitTests/
│   ├── FlowPilot.IntegrationTests/     Tenant isolation, idempotency, consent gate
│   └── FlowPilot.Architecture.Tests/   ArchUnitNET: no cross-module leaks
├── infra/                        Azure Bicep
└── docs/                         Architecture diagrams, sprint plans
```

> Reminder: every `FlowPilot.*` name above is the old working name. Product = **Relora AI**, source tree = **FlowPilot** until the rename lands.

---

## ⚡ Getting started

```bash
# Local infra
docker compose up -d                 # PostgreSQL + Seq

# Backend
dotnet build FlowPilot.sln
dotnet ef database update --project src/FlowPilot.Infrastructure --startup-project src/FlowPilot.Api
dotnet run --project src/FlowPilot.Api

# Frontend (separate terminal)
cd src/FlowPilot.Web
npm install
npm run dev

# Tests
dotnet test FlowPilot.sln
```

API on `https://localhost:7xxx`, web on `http://localhost:5173`. Register a tenant from the UI — the account is seeded with FR + EN templates and a default plan. `start.ps1` / `stop.ps1` at the repo root bring the full stack up and down on Windows.

---

## 📍 Status

Sprint 1 shipped the full MVP demo loop: auth, tenants, customers with consent, appointments, bilingual reminder workflow, public booking, review request flow, real-time dashboard. Sprint 2 is the path to Azure production — column encryption, per-tenant Twilio provisioning, at-risk polish, auto-completion worker. See [`docs/sprint1.md`](docs/sprint1.md) and [`docs/sprint2.md`](docs/sprint2.md).
