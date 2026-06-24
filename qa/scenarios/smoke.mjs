// Smoke scenario — proves the QA harness works end to end and serves as the template
// for new scenarios. Run with:  node scenarios/smoke.mjs   (from the qa/ folder)
//
// It exercises: landing page render, full UI registration → dashboard, and a direct
// API register + authenticated read. Writes a markdown report + screenshots, exits
// non-zero if any check FAILs so CI / the agent can detect regressions.

import {
  startRun, openBrowser, shot, record, writeReport,
  apiRegister, apiFetch, uiRegister, WEB_BASE,
} from "../lib/driver.mjs";

const run = startRun("smoke");
let browser;

try {
  // ---- API probe: register a tenant + authenticated read ------------------
  const reg = await apiRegister();
  record(run, {
    id: "API-AUTH-01",
    title: "POST /auth/register creates a tenant",
    status: reg.status === 201 && reg.accessToken ? "PASS" : "FAIL",
    severity: "Blocker",
    notes: `status=${reg.status}`,
    evidence: reg.accessToken ? "token issued" : JSON.stringify(reg.json),
  });

  const list = await apiFetch("/api/v1/appointments", { token: reg.accessToken });
  record(run, {
    id: "API-APPT-01",
    title: "GET /appointments returns 200 for the new tenant",
    status: list.status === 200 ? "PASS" : "FAIL",
    severity: "Critical",
    notes: `status=${list.status}`,
  });

  // ---- UI: landing renders ------------------------------------------------
  const b = await openBrowser();
  browser = b.browser;
  const page = b.page;

  const landingResp = await page.goto(`${WEB_BASE}/`, { waitUntil: "networkidle" });
  const landingShot = await shot(run, page, "landing");
  record(run, {
    id: "UI-LAND-01",
    title: "Landing page loads (HTTP 200, body visible)",
    status: (landingResp?.status() ?? 0) < 400 ? "PASS" : "FAIL",
    severity: "Critical",
    notes: `http=${landingResp?.status()}`,
    screenshot: landingShot,
  });

  // ---- UI: registration → dashboard --------------------------------------
  const creds = await uiRegister(page);
  const dashShot = await shot(run, page, "dashboard");
  record(run, {
    id: "UI-REG-01",
    title: "UI registration lands on /app dashboard",
    status: /\/app\b/.test(page.url()) ? "PASS" : "FAIL",
    severity: "Blocker",
    notes: `url=${page.url()} email=${creds.email}`,
    screenshot: dashShot,
  });

  // ---- Cross-cutting: no console errors during the journey ---------------
  record(run, {
    id: "UI-CONSOLE-01",
    title: "No console/page errors during smoke journey",
    status: b.consoleErrors.length === 0 ? "PASS" : "WARN",
    severity: b.consoleErrors.length ? "Minor" : "",
    notes: b.consoleErrors.slice(0, 3).join(" | ") || "clean",
  });
} catch (err) {
  record(run, {
    id: "SMOKE-EXC",
    title: "Unhandled exception during smoke run",
    status: "FAIL",
    severity: "Blocker",
    notes: String(err?.message ?? err),
  });
} finally {
  if (browser) await browser.close();
  const reportPath = writeReport(run, {
    note: "Seed smoke scenario.",
    qualityNotes: "_Smoke run — extend with the full scenario catalog for a complete QA pass._",
  });
  const failed = run.results.filter((r) => r.status === "FAIL").length;
  // eslint-disable-next-line no-console
  console.log(`\nReport: ${reportPath}\nFailures: ${failed}`);
  process.exit(failed > 0 ? 1 : 0);
}
