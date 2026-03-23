# MCP Setup — FlowPilot AI

Install exactly 4. Nothing else yet.

---

## 1. GitHub MCP
Manage PRs, issues, search reference code without leaving Claude Code.

**Why for FlowPilot:** Look up Azure SDK examples, check open issues, create PRs from terminal.

```json
{
  "github": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "@modelcontextprotocol/server-github"],
    "env": {
      "GITHUB_PERSONAL_ACCESS_TOKEN": "TODO: your PAT"
    }
  }
}
```
Setup: `brew install gh && gh auth login`
PAT permissions needed: repo (read/write), pull_requests (read/write), issues (read)

---

## 2. Context7 MCP
Pulls live docs for .NET 8, EF Core, Azure Service Bus SDK, Azure OpenAI SDK, Twilio SDK.

**Why for FlowPilot:** The Azure SDKs change fast. Context7 kills hallucinated API signatures.
Usage: append `use context7` to any prompt.
Example: "How do I cancel a deferred Service Bus message via SequenceNumber — use context7"

```json
{
  "context7": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "@upstash/context7-mcp"]
  }
}
```

---

## 3. Memory MCP
Persist architectural decisions and FlowPilot-specific facts across sessions.

**Why for FlowPilot:** Avoid re-explaining the multi-tenancy rules, idempotency keys, and AI boundary every session.

Save things like:
- `# Our Service Bus namespace is sb-flowpilot-dev`
- `# Azure OpenAI deployment name is gpt4o-flowpilot`
- `# Twilio Account SID is AC... (dev)`
- `# We use EF Core snake_case convention`

```json
{
  "memory": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "@modelcontextprotocol/server-memory"],
    "env": {
      "MEMORY_FILE_PATH": "./.claude/memory.json"
    }
  }
}
```

---

## 4. PostgreSQL MCP
Query your local FlowPilot DB directly — inspect schema, debug data, verify EF migrations.

**Why for FlowPilot:** Verify ScheduledAt index exists, check TenantId isolation, inspect EF-generated queries.
Example: "Show me all ScheduledMessages with Status=Pending for tenant X"

```json
{
  "postgres": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "@modelcontextprotocol/server-postgres", "postgresql://localhost/flowpilot_dev"]
  }
}
```

---

## Full Config Block

Paste into Claude Code MCP settings (`~/.claude/claude_desktop_config.json` or the Claude Code MCP panel):

```json
{
  "mcpServers": {
    "github": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "TODO: your PAT"
      }
    },
    "context7": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@upstash/context7-mcp"]
    },
    "memory": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-memory"],
      "env": {
        "MEMORY_FILE_PATH": "./.claude/memory.json"
      }
    },
    "postgres": {
      "type": "stdio",
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-postgres",
        "postgresql://localhost/flowpilot_dev"
      ]
    }
  }
}
```

---

## First Memory Seeds

After installing Memory MCP, run these in Claude Code to seed your context:

```
# TenantId is always resolved from ICurrentTenant (JWT claims), never from request body
# Business rules enforced in C# before any LLM call — consent, cooldowns, business hours
# Idempotency keys: SmsSid (inbound SMS), ExternalId+TenantId (appointment webhook), ProviderMessageId+Status (delivery)
# Scheduler = Azure Service Bus deferred messages. No Hangfire. No cron.
# ServiceBusSequenceNumber stored on every ScheduledMessage for cancellation
# Template fallback chain: Customer.PreferredLanguage → Tenant.DefaultLocale → system default
# EF Core global filters: TenantId + IsDeleted=false on ALL BaseEntity types
# MCP server: NOT in MVP. Build IAgentTool + ToolRegistry first. Extract Phase 3.
```

---

## Skip (add these later)

| MCP | When to add |
|---|---|
| Playwright MCP | When writing E2E tests (Phase 2) |
| Seq/Logging MCP | When debugging production issues |
| Azure MCP | When managing Azure resources from CLI |
| Sequential Thinking | When designing complex agent workflows |
