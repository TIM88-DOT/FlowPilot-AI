# /devops

Launch the autonomous **devops-expert** subagent to audit the CI/CD pipeline + infrastructure
and produce a DevOps hardening report.

Scope (optional): `$ARGUMENTS` — e.g. `workflows only`, `secrets`, `supply chain`, `full sweep`.
If empty, run a full sweep.

Steps:
1. Use the Agent tool with `subagent_type: "devops-expert"`. In the prompt, pass the requested
   scope ($ARGUMENTS, or "full sweep" if none) and instruct it to: sweep `.github/workflows/`,
   `.github/dependabot.yml`, `.gitleaks.toml`, `docker-compose.yml`, and `infra/` (if present);
   verify branch protection / Dependabot via read-only `gh api` where possible; write the
   timestamped report under `qa/reports/`; and change nothing.
2. When it returns, relay to me: the report path, the finding counts by severity, and the top
   hardening steps by confidence. Do not dump raw command output — point me to the report.

Note: the devops-expert is FLAG-ONLY — it audits and reports, it never edits workflows, infra,
or repo settings. Branch-protection and other GitHub-setting changes are surfaced as drafted
commands for you to run, not applied automatically.
