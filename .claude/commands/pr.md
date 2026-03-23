# /pr

Create a GitHub PR for the current branch.

1. Run `git log main..HEAD --oneline` and `git diff main...HEAD --stat`
2. Draft a PR:

**Title:** `<type>(<scope>): <summary>`

**What changed:**
- Bullet list of meaningful changes (not every commit)

**Why:**
1–2 sentences on the problem this solves.

**FlowPilot checklist:**
- [ ] BaseEntity inherited by all new entities
- [ ] TenantId from ICurrentTenant only (not request body)
- [ ] EF Core global filters not bypassed without justification
- [ ] New async methods have CancellationToken
- [ ] Business rules enforced in C# — not inside prompts
- [ ] Idempotency keys handled where applicable (SmsSid / ExternalId / EventId)
- [ ] Soft delete used (IsDeleted) — no hard deletes
- [ ] Integration test added for any new idempotency / tenant isolation path
- [ ] No secrets committed

3. Show me the draft. Wait for approval. Then run `gh pr create --title "..." --body "..."`
