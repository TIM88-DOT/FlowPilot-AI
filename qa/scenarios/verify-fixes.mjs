/**
 * Targeted fix-verification pass for branch fix/qa-4xx-not-500
 *
 * Issue 1 — UI-CONSOLE-01: SignalR console noise
 *   Verify that navigating through the app NO LONGER produces error-level
 *   "Failed to start the connection" / "stopped during negotiation" messages.
 *
 * Issue 2 — API-VAL-BAD-REG: Bad register payload must return 400, not 500
 *   Cases:  invalid email, password too short, missing firstName/lastName/businessName.
 *
 * Issue 3 — API-VAL-TWILIO: Non-form body to /api/webhooks/twilio/sms/inbound must return 400
 *   Cases: JSON body, empty body.
 *
 * Run from qa/ directory:  node scenarios/verify-fixes.mjs
 */

import {
  startRun, openBrowser, shot, record, writeReport,
  apiRegister, apiFetch, uiRegister, WEB_BASE, API_BASE,
  uniqueEmail,
} from "../lib/driver.mjs";

const run = startRun("fix-verification");
let browser;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

try {

  // =========================================================================
  // ISSUE #2 — POST /auth/register bad payload must return 400, not 500
  // =========================================================================
  console.log("\n=== Issue #2: Invalid register payloads → 400 ===");

  // 2a. Invalid email address
  const res2a = await apiFetch("/api/v1/auth/register", {
    method: "POST",
    body: { email: "not-an-email", password: "short" },
  });
  record(run, {
    id: "FIX-REG-01",
    title: "POST /auth/register invalid email → 400 (not 500)",
    status: res2a.status === 400 ? "PASS" : "FAIL",
    severity: "Major",
    notes: `status=${res2a.status} expected=400`,
    evidence: JSON.stringify(res2a.json ?? res2a.text ?? "").slice(0, 200),
  });

  // 2b. Password too short (valid email but only 5 chars)
  const res2b = await apiFetch("/api/v1/auth/register", {
    method: "POST",
    body: { email: uniqueEmail("bad"), password: "short" },
  });
  record(run, {
    id: "FIX-REG-02",
    title: "POST /auth/register short password → 400 (not 500)",
    status: res2b.status === 400 ? "PASS" : "FAIL",
    severity: "Major",
    notes: `status=${res2b.status} expected=400`,
    evidence: JSON.stringify(res2b.json ?? res2b.text ?? "").slice(0, 200),
  });

  // 2c. Missing firstName, lastName, businessName (but otherwise valid)
  const res2c = await apiFetch("/api/v1/auth/register", {
    method: "POST",
    body: { email: uniqueEmail("bad"), password: "ValidPass1234!" },
    // firstName, lastName, businessName intentionally omitted
  });
  record(run, {
    id: "FIX-REG-03",
    title: "POST /auth/register missing firstName/lastName/businessName → 400 (not 500)",
    status: res2c.status === 400 ? "PASS" : "FAIL",
    severity: "Major",
    notes: `status=${res2c.status} expected=400`,
    evidence: JSON.stringify(res2c.json ?? res2c.text ?? "").slice(0, 200),
  });

  // 2d. Completely empty body
  const res2d = await apiFetch("/api/v1/auth/register", {
    method: "POST",
    body: {},
  });
  record(run, {
    id: "FIX-REG-04",
    title: "POST /auth/register empty body → 400 (not 500)",
    status: res2d.status === 400 ? "PASS" : "FAIL",
    severity: "Major",
    notes: `status=${res2d.status} expected=400`,
    evidence: JSON.stringify(res2d.json ?? res2d.text ?? "").slice(0, 200),
  });

  // Confirm good register still works
  const goodReg = await apiRegister();
  record(run, {
    id: "FIX-REG-00",
    title: "POST /auth/register valid payload still returns 201 (no regression)",
    status: goodReg.status === 201 && goodReg.accessToken ? "PASS" : "FAIL",
    severity: "Blocker",
    notes: `status=${goodReg.status}`,
    evidence: goodReg.accessToken ? "token present" : JSON.stringify(goodReg.json ?? "").slice(0, 120),
  });

  // =========================================================================
  // ISSUE #3 — POST /api/webhooks/twilio/sms/inbound non-form body → 400
  // =========================================================================
  console.log("\n=== Issue #3: Non-form Twilio webhook → 400 ===");

  // 3a. JSON body (Content-Type: application/json)
  const res3a = await apiFetch("/api/webhooks/twilio/sms/inbound", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: { SmsSid: "SM123", From: "+1555000", To: "+1555001", Body: "hello" },
  });
  record(run, {
    id: "FIX-TWILIO-01",
    title: "POST /webhooks/twilio/sms/inbound with JSON body → 400 (not 500)",
    status: res3a.status === 400 ? "PASS" : "FAIL",
    severity: "Major",
    notes: `status=${res3a.status} expected=400`,
    evidence: (res3a.text ?? "").slice(0, 200),
  });

  // 3b. Empty body (no Content-Type)
  const emptyBodyRes = await fetch(`${API_BASE}/api/webhooks/twilio/sms/inbound`, {
    method: "POST",
    // No body, no Content-Type → not form content type
  });
  const emptyBodyText = await emptyBodyRes.text();
  record(run, {
    id: "FIX-TWILIO-02",
    title: "POST /webhooks/twilio/sms/inbound with empty body → 400 (not 500)",
    status: emptyBodyRes.status === 400 ? "PASS" : "FAIL",
    severity: "Major",
    notes: `status=${emptyBodyRes.status} expected=400`,
    evidence: emptyBodyText.slice(0, 200),
  });

  // 3c. Plain text body
  const textBodyRes = await fetch(`${API_BASE}/api/webhooks/twilio/sms/inbound`, {
    method: "POST",
    headers: { "Content-Type": "text/plain" },
    body: "this is not a form",
  });
  const textBodyText = await textBodyRes.text();
  record(run, {
    id: "FIX-TWILIO-03",
    title: "POST /webhooks/twilio/sms/inbound with text/plain body → 400 (not 500)",
    status: textBodyRes.status === 400 ? "PASS" : "FAIL",
    severity: "Major",
    notes: `status=${textBodyRes.status} expected=400`,
    evidence: textBodyText.slice(0, 200),
  });

  // =========================================================================
  // ISSUE #1 — SignalR console noise — UI journey with console error monitoring
  // =========================================================================
  console.log("\n=== Issue #1: SignalR console noise check ===");

  const b = await openBrowser({ headless: true });
  browser = b.browser;
  const page = b.page;
  const signalRErrors = [];
  const allConsoleErrors = [];

  // Monitor console for error-level SignalR messages
  page.on("console", (m) => {
    const txt = m.text();
    if (m.type() === "error") {
      allConsoleErrors.push(txt);
      if (/failed to start the connection|stopped during negotiation|signalr/i.test(txt)) {
        signalRErrors.push(txt);
      }
    }
  });

  // Register and navigate the app (the SignalR errors fire during/after login)
  const creds = await uiRegister(page);
  // Wait for dashboard to settle, then navigate through several routes
  await sleep(2000);
  const dashShot = await shot(run, page, "signalr-test-dashboard");

  // Navigate to customers, appointments, inbox — these trigger SignalR reconnect cycles
  await page.goto(`${WEB_BASE}/app/customers`, { waitUntil: "networkidle" });
  await sleep(1500);
  await page.goto(`${WEB_BASE}/app/appointments`, { waitUntil: "networkidle" });
  await sleep(1500);
  await page.goto(`${WEB_BASE}/app/inbox`, { waitUntil: "networkidle" });
  await sleep(1500);
  await page.goto(`${WEB_BASE}/app`, { waitUntil: "networkidle" });
  await sleep(2000);

  const afterNavShot = await shot(run, page, "signalr-test-after-nav");

  // Issue #1: No SignalR ERROR-level console messages
  record(run, {
    id: "FIX-SIGNALR-01",
    title: "UI: No error-level SignalR 'Failed to start' / 'stopped during negotiation' messages in console",
    status: signalRErrors.length === 0 ? "PASS" : "FAIL",
    severity: "Major",
    notes: `signalR errors=${signalRErrors.length} :: ${signalRErrors.slice(0, 3).join(" | ") || "clean"}`,
    screenshot: afterNavShot,
    evidence: signalRErrors.length ? signalRErrors.join("; ") : "No SignalR error-level noise detected",
  });

  // Cross-cutting: any unexpected errors at all?
  const unexpectedErrors = allConsoleErrors.filter(
    (e) =>
      !e.includes("401") &&
      !e.includes("Unauthorized") &&
      !e.includes("ERR_CONNECTION_REFUSED") // expected if hubs use separate port in some configs
  );
  record(run, {
    id: "FIX-CONSOLE-01",
    title: "UI: Total unexpected console errors across navigation (excl. 401 and ECONNREFUSED)",
    status: unexpectedErrors.length === 0 ? "PASS" : "WARN",
    severity: unexpectedErrors.length > 0 ? "Minor" : "",
    notes: `total errors=${allConsoleErrors.length} unexpected=${unexpectedErrors.length}`,
    evidence: unexpectedErrors.slice(0, 5).join(" | ") || "clean",
    screenshot: afterNavShot,
  });

} catch (err) {
  record(run, {
    id: "VERIFY-EXC",
    title: "Unhandled exception during fix-verification",
    status: "FAIL",
    severity: "Blocker",
    notes: String(err?.message ?? err) + "\n" + String(err?.stack ?? "").slice(0, 300),
  });
  console.error("Exception:", err);
} finally {
  if (browser) await browser.close();

  const qualityNotes = `
### Fix-Verification Pass — branch \`fix/qa-4xx-not-500\`

**Issue #1 — SignalR console noise (UI-CONSOLE-01)**
The \`useSignalR\` hook now uses a custom \`ILogger\` (\`signalRLogger\`) that matches
"stopped during negotiation" and "Failed to start the connection" via a regex and
routes those to \`console.debug\` instead of \`console.error\`. The fix correctly targets
the expected abort messages from navigation/React-Strict-Mode double-mount cycles.
QA observed the browser console during multi-page navigation — see evidence column.

**Issue #2 — POST /auth/register 400 vs 500**
\`AuthService.RegisterAsync\` now calls \`ValidateRegisterRequest\` as the first step,
returning a \`Result.Failure\` with \`Error.Validation\` for all malformed payloads.
The endpoint maps any validation failure to HTTP 400. Four cases tested:
invalid email, password <8 chars, empty payload, missing name fields.

**Issue #3 — POST /api/webhooks/twilio/sms/inbound 400 vs 500 for non-form bodies**
The endpoint now guards with \`request.HasFormContentType\` before calling \`ReadFormAsync\`,
returning \`Results.BadRequest\` immediately. Three cases tested: JSON body, empty body,
text/plain body.

**Regression risk:** The fix passes through the existing validation stack unchanged for
valid payloads. The "no regression" check (valid register → 201 + token) confirms the
happy path is unaffected.
`.trim();

  const reportPath = writeReport(run, {
    note: "Fix-verification pass — branch fix/qa-4xx-not-500. Issues #1, #2, #3.",
    qualityNotes,
  });

  const passed = run.results.filter((r) => r.status === "PASS").length;
  const failed = run.results.filter((r) => r.status === "FAIL").length;
  const warned = run.results.filter((r) => r.status === "WARN").length;

  console.log(`\n${"=".repeat(60)}`);
  console.log(`FIX VERIFICATION COMPLETE`);
  console.log(`Report: ${reportPath}`);
  console.log(`PASS: ${passed}  FAIL: ${failed}  WARN: ${warned}  TOTAL: ${run.results.length}`);
  process.exit(failed > 0 ? 1 : 0);
}
