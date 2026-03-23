# /test

Run tests for the current area and fix failures.

1. Detect what's in scope (recent edited files):
   - `.cs` files in `src/` → `dotnet test FlowPilot.sln --filter 'Category!=Integration'`
   - `.tsx`/`.ts` files → `cd src/FlowPilot.Web && npm test`
   - Integration tests explicitly → `dotnet test --filter 'Category=Integration'`

2. Show output, highlighting failures only (skip passing test names)

3. For each failure:
   - Read the actual error — don't guess
   - Fix code or test (fix code first unless the assertion is provably wrong)
   - Re-run to confirm green

4. Final report: X passed, Y failed → fixed, Z still failing (if any)

---

FlowPilot integration test categories to know:
- `TenantIsolation` — cross-tenant query leaks
- `Idempotency` — SmsSid, ExternalId, EventId
- `SoftDelete` — IsDeleted filter
- `ConsentGate` — outbound send blocked without consent
- `CancellationCascade` — appointment cancel → Service Bus SequenceNumber cancel
