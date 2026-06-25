# /janitor

FlowPilot code janitor — finds dead code, unused tools, and cruft. **Flag-only: makes no changes.**

Scope defaults to the **whole codebase**. If the caller names a scope (e.g. "just the agent tools",
"the web app", "the diff"), honor it. For a diff-only pass, run `git diff main...HEAD` first.

Detect, then output a single prioritized report. Never edit, delete, or run formatters that rewrite
files — your job is to surface candidates with evidence so the human decides. Every finding needs a
`path:line` and a one-line reason. When a finding *might* be a false positive (reflection, DI,
public API, dynamic dispatch), say so and lower its confidence.

---

### 🔴 DEAD / UNUSED — safe-ish to remove after a human glance

**Agent tools (FlowPilot-specific — check this first)**
- [ ] Every `IAgentTool` implementation under `Agents/Tools/` is registered via `registry.Register(...)`
      in `src/FlowPilot.Api/Program.cs`. List any impl that is **not** registered (dead tool).
- [ ] Every registered tool is actually reachable (referenced by an agent/orchestrator). Flag
      registered-but-never-invoked tools.

**C# dead code**
- [ ] Classes / `record`s / interfaces with zero references across the solution
- [ ] Unused `using` directives; unreferenced `private` methods and fields
- [ ] DTOs/records defined but never constructed or returned
- [ ] MediatR: handlers for events nobody publishes; events published but never handled
- [ ] NuGet packages in a `.csproj` whose namespace is never imported anywhere in that project

**Frontend dead code**
- [ ] Exported components/hooks/utils with no importer (`npx ts-prune` if available, else grep)
- [ ] Unreferenced files under `src/FlowPilot.Web/src/` (not in any route or import graph)
- [ ] npm dependencies in `package.json` never imported (`npx depcheck` if available)

---

### 🟡 SMELLS — debt to clean, not strictly dead

- [ ] Commented-out code blocks (not doc comments) — flag for deletion
- [ ] `TODO` / `FIXME` / `HACK` markers — list with location and age if cheap to get
- [ ] `.IgnoreQueryFilters()` **without** an explanatory comment (CLAUDE.md violation)
- [ ] `.Result` / `.Wait()` (deadlock risk) — `async` should flow through
- [ ] `console.log` in React (CLAUDE.md says structured logging only)
- [ ] `any` in TypeScript (strict-mode violation)
- [ ] Duplicated helpers / near-identical blocks worth consolidating
- [ ] Empty files, empty catch blocks, unreachable code after `return`/`throw`

---

### 🟢 NICE TO HAVE
- Stale feature flags / config keys not read anywhere
- Test files for code that no longer exists
- Orphaned EF migration artifacts (verify carefully — usually keep)

---

### Detection commands (read-only)
```bash
dotnet build FlowPilot.sln                       # warnings surface unused usings/vars
npx ts-prune --error 2>/dev/null || true         # unused TS exports (one-off, no install)
npx depcheck src/FlowPilot.Web 2>/dev/null || true  # unused npm deps
```
Cross-check tool output against grep before trusting it — analyzers miss DI/reflection wiring.

### Output
A prioritized list grouped 🔴 / 🟡 / 🟢, each item: `path:line` — what — why — confidence (and
false-positive caveat if any). Skip empty sections. End with a 2-line summary: total candidates and
the safest 3–5 to remove first. **Do not make any edits.**
