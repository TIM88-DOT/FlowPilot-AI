# /qa

Launch the autonomous **qa-tester** subagent to test the running app and produce a QA report.

Scope (optional): `$ARGUMENTS` — e.g. `booking + appointments`, `full sweep`, `just auth`.
If empty, run a full sweep.

Steps:
1. Use the Agent tool with `subagent_type: "qa-tester"`. In the prompt, pass the requested scope
   ($ARGUMENTS, or "full sweep" if none) and instruct it to: boot the stack via `qa-up.ps1`, run the
   relevant scenarios from its catalog (Playwright UI + API probes), write the timestamped report
   under `qa/reports/`, and tear the stack down with `stop.ps1`.
2. When it returns, relay to me: the report path(s), the pass/fail/warn counts, and the top issues by
   severity. Do not dump raw Playwright logs — point me to the report and screenshots.

Note: the qa-tester drives a real browser and starts/stops the dev stack, so it needs a free
`:5216` and `:5173`. First run on a machine downloads headless Chromium (one-time).
