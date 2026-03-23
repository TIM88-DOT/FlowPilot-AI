# /commit

Generate a conventional commit for staged changes — FlowPilot scoped.

1. Run `git diff --cached --stat` and `git diff --cached`
2. Identify the primary bounded context changed:
   - `tenants` | `auth` | `customers` | `appointments` | `messaging` | `campaigns` | `agents` | `billing` | `analytics` | `infra` | `web`
3. Generate commit message:
   ```
   <type>(<scope>): <summary under 72 chars>

   <optional body: what changed and why — not how>
   ```
   Types: `feat` | `fix` | `chore` | `docs` | `refactor` | `test` | `perf`

4. Show me the message. Wait for my confirmation, then run `git commit -m "..."`

Special cases to flag:
- Changes to `BaseEntity` or EF Core global filters → warn about migration impact
- Changes to `IAgentTool`, `IToolRegistry`, or agent prompts → flag AI boundary change
- Changes to idempotency logic (SmsSid, ExternalId, EventId) → flag risk level HIGH
