// FlowPilot QA driver — reusable primitives for autonomous UI + API testing.
// The qa-tester agent composes scenario scripts from these helpers. Keep it dependency-light:
// only `playwright` + Node built-ins (global fetch requires Node 18+).

import { chromium } from "playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import { join, dirname, relative } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
export const QA_ROOT = join(__dirname, "..");
export const REPORTS_DIR = join(QA_ROOT, "reports");

// The web app proxies /api and /hubs to the API, so UI traffic goes through :5173
// while direct API probes hit :5216. Override via env for non-default setups.
export const WEB_BASE = process.env.QA_WEB_BASE ?? "http://localhost:5173";
export const API_BASE = process.env.QA_API_BASE ?? "http://localhost:5216";

const STATUS = { PASS: "PASS", FAIL: "FAIL", WARN: "WARN", INFO: "INFO" };
export { STATUS };

/** Timestamp like 20260624-141233, safe for filenames. */
export function stamp(d = new Date()) {
  const p = (n) => String(n).padStart(2, "0");
  return (
    `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}` +
    `-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`
  );
}

export function uniqueEmail(prefix = "qa") {
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e4)}@qa.test`;
}

export function randPhone() {
  return "+1416" + Math.floor(1000000 + Math.random() * 8999999);
}

/**
 * Start a QA run: makes the screenshot dir and an in-memory results accumulator.
 * @returns {{ runId:string, startedAt:Date, shotDir:string, results:Array, scope:string }}
 */
export function startRun(scope = "full") {
  const runId = stamp();
  const shotDir = join(REPORTS_DIR, "screenshots", runId);
  mkdirSync(shotDir, { recursive: true });
  return { runId, startedAt: new Date(), shotDir, results: [], scope };
}

/** Launch a Chromium browser + page. Captures console errors and failed requests. */
export async function openBrowser({ headless = true, viewport = { width: 1366, height: 900 } } = {}) {
  const browser = await chromium.launch({ headless });
  const context = await browser.newContext({ viewport });
  const page = await context.newPage();
  const consoleErrors = [];
  const failedRequests = [];
  page.on("console", (m) => {
    if (m.type() === "error") consoleErrors.push(m.text());
  });
  page.on("pageerror", (e) => consoleErrors.push(String(e)));
  page.on("requestfailed", (r) =>
    failedRequests.push(`${r.method()} ${r.url()} — ${r.failure()?.errorText ?? "failed"}`)
  );
  return { browser, context, page, consoleErrors, failedRequests };
}

/** Full-page screenshot saved under the run's shot dir. Returns the absolute path. */
export async function shot(run, page, label) {
  const idx = String(run.results.length).padStart(2, "0");
  const safe = label.replace(/[^a-z0-9-_]+/gi, "-").toLowerCase();
  const file = join(run.shotDir, `${idx}-${safe}.png`);
  await page.screenshot({ path: file, fullPage: true });
  return file;
}

/**
 * Record one check.
 * @param {object} run
 * @param {{id:string,title:string,status:keyof STATUS,severity?:string,notes?:string,screenshot?:string,evidence?:string}} entry
 */
export function record(run, entry) {
  const e = { severity: "", notes: "", screenshot: "", evidence: "", ...entry };
  run.results.push(e);
  const tag = e.severity ? `${e.status}/${e.severity}` : e.status;
  // eslint-disable-next-line no-console
  console.log(`[${tag}] ${e.id} — ${e.title}${e.notes ? ` :: ${e.notes}` : ""}`);
  return e;
}

/** Direct API call against :5216. Returns { status, ok, json, text }. */
export async function apiFetch(path, { method = "GET", token, body, headers = {} } = {}) {
  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    /* non-JSON */
  }
  return { status: res.status, ok: res.ok, json, text };
}

/** Register a fresh tenant via the API. Returns { accessToken, user, email, password }. */
export async function apiRegister(overrides = {}) {
  const email = overrides.email ?? uniqueEmail("api");
  const password = overrides.password ?? "QaTest1234!@#";
  const body = {
    email,
    password,
    firstName: "Qa",
    lastName: "Bot",
    businessName: overrides.businessName ?? `QA Salon ${Date.now()}`,
    businessPhone: "+14165551234",
    timezone: "America/Toronto",
    defaultLanguage: "en",
    ...overrides,
  };
  const res = await apiFetch("/api/v1/auth/register", { method: "POST", body });
  return { ...res, email, password, accessToken: res.json?.accessToken, user: res.json?.user };
}

/** Register a tenant through the actual UI form. Lands on /app on success. */
export async function uiRegister(page, { email = uniqueEmail("ui"), password = "QaTest1234!@#" } = {}) {
  await page.goto(`${WEB_BASE}/register`, { waitUntil: "networkidle" });
  // NOTE: register labels are NOT linked to inputs — use placeholders, not getByLabel.
  await page.getByPlaceholder("Jane").fill("Qa");
  await page.getByPlaceholder("Doe").fill("Bot");
  await page.getByPlaceholder("Salon Belleza").fill(`QA Salon ${Date.now()}`);
  await page.getByPlaceholder("you@business.com").fill(email);
  await page.getByPlaceholder("••••••••").fill(password);
  await page.getByRole("button", { name: /create account/i }).click();
  await page.waitForURL(/\/app\b/, { timeout: 15000 });
  return { email, password };
}

const severityRank = { Blocker: 0, Critical: 1, Major: 2, Minor: 3, Cosmetic: 4, "": 9 };

/** Write a markdown report for the run. Returns the report path. */
export function writeReport(run, meta = {}) {
  const finishedAt = new Date();
  const counts = run.results.reduce((acc, r) => {
    acc[r.status] = (acc[r.status] ?? 0) + 1;
    return acc;
  }, {});
  const fails = run.results
    .filter((r) => r.status === "FAIL")
    .sort((a, b) => (severityRank[a.severity] ?? 9) - (severityRank[b.severity] ?? 9));

  const rel = (abs) => (abs ? relative(REPORTS_DIR, abs).replace(/\\/g, "/") : "");
  const row = (r) =>
    `| ${r.id} | ${r.title} | ${r.status} | ${r.severity || "—"} | ${(r.notes || "").replace(/\|/g, "\\|")} | ${
      r.screenshot ? `[shot](${rel(r.screenshot)})` : "—"
    } |`;

  const md = `# QA Report — ${run.runId}

- **Scope:** ${run.scope}
- **Started:** ${run.startedAt.toISOString()}
- **Finished:** ${finishedAt.toISOString()}
- **Web:** ${WEB_BASE}  ·  **API:** ${API_BASE}
- **Result:** ${counts.PASS ?? 0} passed · ${counts.FAIL ?? 0} failed · ${counts.WARN ?? 0} warnings${
    meta.note ? `\n- **Note:** ${meta.note}` : ""
  }

## ${fails.length ? "❌ Failures (by severity)" : "✅ No failures"}

${
  fails.length
    ? fails.map((r) => `- **[${r.severity || "Unrated"}] ${r.id} — ${r.title}**: ${r.notes || ""}${
        r.evidence ? `\n  - evidence: ${r.evidence}` : ""
      }${r.screenshot ? `\n  - screenshot: \`${rel(r.screenshot)}\`` : ""}`).join("\n")
    : "All executed checks passed."
}

## All checks

| ID | Check | Status | Severity | Notes | Evidence |
|----|-------|--------|----------|-------|----------|
${run.results.map(row).join("\n")}

## Build-quality notes
${meta.qualityNotes ?? "_Add observations the team should act on while building (UX rough edges, slow endpoints, missing empty states, console noise, accessibility gaps)._"}
`;

  mkdirSync(REPORTS_DIR, { recursive: true });
  const reportPath = join(REPORTS_DIR, `qa-report-${run.runId}.md`);
  writeFileSync(reportPath, md, "utf8");
  return reportPath;
}
