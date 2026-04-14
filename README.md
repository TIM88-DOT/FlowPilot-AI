# Relora AI

**Appointments that manage themselves.**

An AI-native communication OS for appointment-based small businesses — salons, clinics, barbers, studios, dentists. Bilingual (FR + EN) from day one, built for the Canadian market.

> **Heads up — the product was renamed.** This repository is still called `FlowPilot` and the .NET solution, projects, and namespaces are all `FlowPilot.*`. That's the old working name. The product is now **Relora AI** everywhere the user sees it (landing page, app UI, marketing). The source-tree rename is planned but not yet done, so expect `FlowPilot` in paths, `dotnet` commands, and assembly names throughout.

---

## Why this exists

Small businesses don't lose money because they're bad at their craft. They lose it in the gaps between appointments — the no-shows, the silent customers, the reviews that never got asked for, the reminder that went out in the wrong language.

The owner of a 3-chair salon does not want a CRM. They don't want another dashboard to check. They want the *outcome*: clients show up on time, the chair stays full, the 5-star reviews keep rolling in. Everything else is overhead.

Existing tools make them work harder:

- **Booking software** sends dumb reminders at fixed hours. A client who always replies at 9pm gets pinged at 10am and forgets by evening.
- **Marketing tools** spam everyone with the same campaign regardless of language, history, or consent.
- **Review platforms** ask for feedback at random — usually right after a rushed appointment, never after the ones the client actually loved.
- **Everything is in English.** In Montreal, Québec City, Moncton, and half of Ontario, that's a non-starter.

Relora flips this. The AI *is* the workflow — not a chatbot bolted onto one. It decides when to remind, in which language, using which tone. It reads incoming replies and updates the appointment without a human touching a keyboard. It waits for the right moment to ask for a review, and only asks the clients likely to leave a good one. The owner watches it happen.

**The bet:** the next generation of SMB software won't have settings pages. It will have outcomes, and an AI agent that takes responsibility for them.

---

## What it does

### Smart bilingual reminders
Every client has a preferred language (FR or EN). Every SMS — reminder, confirmation, reschedule, review request — is rendered in that language with templates the tenant can override. An LLM-driven agent picks the optimal send time per client based on past response patterns, not a static 24h-before rule.

### Conversational SMS, both directions
Clients reply in plain language — "can I move to Thursday?", "need to cancel", "see you at 3". Inbound SMS is classified by intent, matched to the appointment, and the system responds or reschedules without anyone in the back office lifting a finger. A reschedule link in the reply lets the client pick a new slot themselves on a mobile-first public booking page.

### Automatic review recovery
After a completed appointment, the system waits for the right window, checks a 30-day cooldown, and sends a review request — but only to clients the AI is confident will leave a positive one. The link drops them directly on the tenant's Google, Facebook, or Trustpilot page.

### Public booking without an account
Every tenant gets a shareable `/book/{slug}` URL. No app install, no signup — clients pick a service, pick a slot, enter their phone, confirm. The system handles consent capture, deduplication by phone number, and auto-schedules the reminder workflow the moment the booking lands.

### No-show scoring and at-risk flags
Every customer carries a rolling no-show score. Appointments flagged as at-risk get an extra confirmation touch; confirmed ones get auto-completed when the end time passes. The owner sees one number, not a spreadsheet.

### Owner-grade dashboard
Live appointment feed, real-time updates (SignalR), weekly stats, upcoming at-risk list. Designed to be glanceable on a phone between clients, not studied from a laptop.

---

## Design principles

### The application is always the source of truth
Business rules never live inside a prompt. Consent checks, cooldown windows, business-hours gates, status transitions — all deterministic C# that runs *before* any LLM call. The AI decides timing, tone, and intent classification. The code enforces what is legal and what is possible.

### Multi-tenant isolation is not a feature, it's a precondition
Every entity carries a `TenantId`. Every query is filtered by EF Core global filters. Every Service Bus message validates its tenant before processing. Cross-tenant isolation tests run on every CI build. A tenant leak is a product-ending bug, so it's built to be impossible, not unlikely.

### Privacy-first by default
Soft delete everywhere. GDPR-style anonymization on customer delete (PII wiped, not just marked dead). Consent is append-only — you can always prove when, where, and how a customer opted in. Column-level encryption on phone and email (in progress).

### Events, not cron jobs
There is no Hangfire, no Quartz, no background cron. Scheduling is Azure Service Bus deferred messages with sequence numbers stored on the row, so anything can be cancelled deterministically when a client reschedules or cancels. Everything interesting in the system is an event someone else can listen to.

### One monolith, nine bounded contexts
Tenants · Identity & Auth · Customers · Appointments · Messaging · Campaigns · AI/Agents · Billing · Analytics. Modules never touch each other's `DbContext` — ArchUnitNET tests fail the build if they do. This keeps the option open to split a module into a service later without a rewrite.

---

## Tech

### Backend
- **.NET 8** minimal APIs, MediatR for in-process domain events, `Result<T>` for all service boundaries
- **EF Core 8** on **PostgreSQL**, snake_case, global query filters on every entity
- **Azure Service Bus** for scheduled messages, integration events, and worker queues
- **Azure OpenAI** (`Azure.AI.OpenAI`) for reminder optimization, intent classification, review confidence scoring
- **Twilio** SMS behind `ISmsProvider` (pluggable — a fake provider runs in tests and local dev)
- **SignalR** for real-time dashboard updates
- **Seq** for structured logs locally

### Frontend
- **React 18 + TypeScript** (strict, no `any`)
- **TanStack Query** for all server state, **React Hook Form + Zod** for all forms
- **Tailwind + shadcn/ui** — Mintlify-inspired design system, documented in [`DESIGN.md`](DESIGN.md)
- JWT in memory only, refresh token in `httpOnly` cookie
- Code-split public booking flow, mobile-first

### Infra
- **Azure Bicep** in `infra/`
- **Docker Compose** for local PostgreSQL + Seq

---

## Repository layout

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

## Getting started

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

The API comes up on `https://localhost:7xxx`, the web app on `http://localhost:5173`. Register a tenant from the web UI — the account gets seeded with FR + EN system templates and a default plan.

A one-shot `start.ps1` / `stop.ps1` pair at the repo root brings the full stack up and down on Windows.

---

## Status

Sprint 1 shipped the full MVP demo loop end-to-end: auth, tenants, customers with consent, appointments, bilingual reminder workflow, public booking, review request flow, real-time dashboard. Sprint 2 is the path to Azure production — column encryption, Twilio per-tenant provisioning, at-risk flagging polish, and the auto-completion worker. See [`docs/sprint1.md`](docs/sprint1.md) and [`docs/sprint2.md`](docs/sprint2.md) for the detailed trail.

---

## License

Proprietary. All rights reserved.
