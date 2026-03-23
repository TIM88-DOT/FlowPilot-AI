# /review

Senior .NET + React code review — FlowPilot architecture lens.

Run `git diff main...HEAD` and review everything changed.

---

### 🔴 MUST FIX — will cause bugs or data leaks in production

**Multi-tenancy**
- [ ] TenantId resolved from JWT (`ICurrentTenant`), never from request body
- [ ] No raw DB queries that could bypass EF Core global filters
- [ ] Service Bus message handler validates TenantId before processing

**AI Boundary**
- [ ] Consent check happens in C# BEFORE any LLM call
- [ ] 30-day review cooldown enforced in C# BEFORE LLM call
- [ ] Business hours gate in MessagingService — not inside a prompt
- [ ] Business rules (status transitions, rate limits) in domain code, not prompt

**Idempotency**
- [ ] Inbound SMS handler checks `ProviderSmsSid` before any processing
- [ ] Appointment webhook checks `ExternalId + TenantId` unique constraint
- [ ] Delivery status webhook uses upsert on `ProviderMessageId + Status`
- [ ] Service Bus consumer checks `ProcessedEvents` table

**Scheduling**
- [ ] `ServiceBusSequenceNumber` stored on every `ScheduledMessage`
- [ ] Appointment cancellation cascades to cancel Service Bus deferred messages

**Security**
- [ ] No connection strings or API keys in code
- [ ] Phone/email fields going through column encryption
- [ ] Structured logs contain IDs only — no phone numbers, no names

---

### 🟡 SHOULD FIX — tech debt or correctness issues

**C# Quality**
- [ ] No `.Result` or `.Wait()` (deadlock risk)
- [ ] `CancellationToken` passed through full call chain
- [ ] `AsNoTracking()` on read-only EF queries
- [ ] `IDisposable` resources disposed (use `await using`)
- [ ] No `var` where type is non-obvious

**React / TypeScript**
- [ ] No `any` types
- [ ] useEffect dependency arrays correct and complete
- [ ] Loading + error states handled (not just happy path)
- [ ] No JWT/token stored in localStorage (memory only)

**Architecture**
- [ ] No module directly imports another module's DbContext or repository
- [ ] New entities inherit `BaseEntity`
- [ ] Soft delete used — no hard deletes

---

### 🟢 NICE TO HAVE
- XML doc on new public interfaces
- Meaningful error messages in `Result<T>` failures
- New component > 200 lines → suggest split

Output prioritized list only. Skip sections with no findings.
