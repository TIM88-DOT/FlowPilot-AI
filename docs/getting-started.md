# Getting Started — FlowPilot Dev Environment

Work through this once, top to bottom. Takes ~1.5 hours.

---

## Phase 1 — Drop the Files (15 min)

- [ ] Copy `CLAUDE.md` to your project root
- [ ] Copy `global-CLAUDE.md` to `~/.claude/CLAUDE.md` (create folder if needed)
- [ ] Copy `.claude/settings.json` to your project's `.claude/` folder
- [ ] Copy `.claude/settings.local.json` to `.claude/` — add to `.gitignore`
- [ ] Copy `.claude/commands/` folder to your project
- [ ] Copy `docs/sprint1.md` to your `docs/` folder
- [ ] Copy `docker-compose.yml` to project root
- [ ] Add these to `.gitignore`:
  ```
  .claude/settings.local.json
  .claude/memory.json
  .claude/.sessions/
  **/appsettings.Production.json
  **/appsettings.Staging.json
  .env
  ```
- [ ] Commit `CLAUDE.md`, `.claude/settings.json`, `.claude/commands/`, `docs/sprint1.md`

---

## Phase 2 — Local Infra (20 min)

- [ ] Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8
- [ ] Install Node 20+: `brew install node` or https://nodejs.org
- [ ] Install Docker Desktop: https://www.docker.com/products/docker-desktop
- [ ] Install GitHub CLI: `brew install gh && gh auth login`
- [ ] Install EF Core tools: `dotnet tool install --global dotnet-ef`
- [ ] Start local infra: `docker compose up -d`
- [ ] Verify:
  - Postgres: `psql postgresql://flowpilot:flowpilot_dev_pass@localhost/flowpilot_dev -c '\l'`
  - Seq UI: http://localhost:8081

---

## Phase 3 — MCPs (30 min)

- [ ] Create GitHub PAT: GitHub → Settings → Developer Settings → Fine-grained tokens
  - Permissions: repos (r/w), pull_requests (r/w), issues (r)
- [ ] Open Claude Code MCP settings panel
- [ ] Paste full config block from `docs/mcp-setup.md`
- [ ] Replace `TODO: your PAT` with your actual PAT
- [ ] Replace Postgres connection string with: `postgresql://flowpilot:flowpilot_dev_pass@localhost/flowpilot_dev`
- [ ] Restart Claude Code
- [ ] Test GitHub MCP: "list my open GitHub issues"
- [ ] Test Context7: "how do I cancel a deferred Azure Service Bus message by SequenceNumber — use context7"
- [ ] Test Postgres MCP: "show me all tables in flowpilot_dev" (after Migration 001)
- [ ] Seed memory (run in Claude Code):
  ```
  # TenantId always from ICurrentTenant (JWT), never request body
  # Scheduler = Azure Service Bus deferred messages, no Hangfire, no cron
  # ServiceBusSequenceNumber stored on every ScheduledMessage for cancellation
  # Business rules in C# before LLM: consent, cooldowns, business hours gates
  # Idempotency: SmsSid (inbound SMS), ExternalId+TenantId (appt webhook), ProviderMessageId+Status (delivery)
  ```

---

## Phase 4 — Start Building (ongoing)

Use `/sprint` to see what to build next.
Use `use context7` whenever working with Azure SDK or EF Core.
Use `/commit`, `/review`, `/pr` for every change.
Use `/test` when tests fail.

**Start here:** Day 1–2 of `docs/sprint1.md` — Solution + BaseEntity + Docker Compose + Migration 001.

---

## Useful Claude Prompts to Start With

```
# Start a new feature
"I'm starting Day 3-4 of Sprint 1 (Auth & Tenant). 
Read CLAUDE.md and docs/sprint1.md, then implement the JWT middleware 
and ICurrentTenant service."

# Get unstuck on Azure SDK
"How do I schedule a deferred message on Azure Service Bus and store 
the SequenceNumber — use context7"

# Check your work
"/review"

# Debug EF Core query
"The query for ScheduledMessages isn't filtering by TenantId. 
Here's the code: [paste]. Check if the EF Core global filter is applied correctly."
```
