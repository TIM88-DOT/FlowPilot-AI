# FlowPilot QA Harness

Autonomous QA for the running app: **Playwright** UI journeys at `:5173` + direct **API**
probes at `:5216`. Driven by the [`qa-tester`](../.claude/agents/qa-tester.md) agent
(launch with `/qa`), but every piece is runnable by hand.

## One-time setup
```bash
cd qa
npm install
npm run install:browser   # downloads headless Chromium (~150 MB)
```

## Run the app, then test
The `qa-tester` agent boots the stack with allowlisted `dotnet`/`npm` commands (this environment
denies PowerShell). To do it by hand:
```bash
# from repo root
docker compose up -d
dotnet ef database update --project src/FlowPilot.Infrastructure --startup-project src/FlowPilot.Api
dotnet run --project src/FlowPilot.Api --launch-profile http      # API :5216  (leave running)
npm run dev --prefix src/FlowPilot.Web                            # Web :5173  (leave running)

npm run smoke --prefix qa    # seed journey → qa/reports/qa-report-<timestamp>.md

# teardown: stop the two servers above, then
docker compose down
```
> `qa-up.ps1` / `stop.ps1` at the repo root do the same in one shot, but they're PowerShell — run
> them yourself with the `! ` prefix (e.g. `! ./qa-up.ps1`); the agent can't, due to the deny rule.

## Layout
| Path | Purpose |
|------|---------|
| `lib/driver.mjs` | Reusable primitives: browser launch, screenshots, API client, register helpers, markdown report writer |
| `scenarios/*.mjs` | One script per journey. `smoke.mjs` is the template |
| `reports/` | Generated `qa-report-<ts>.md` + `screenshots/<ts>/` (git-ignored) |

## Writing a scenario
Import from `lib/driver.mjs`, `record()` each check with a severity, `shot()` for evidence,
then `writeReport()`. Selector gotcha: **form labels are not linked to inputs** — use
`getByPlaceholder` / `getByRole` / `getByText`, not `getByLabel`. The app proxies `/api`
through Vite, so UI runs hit `:5173` and direct probes hit `:5216`.
