# Global Claude Preferences — FlowPilot Dev

## My Style
- Solo developer building FlowPilot AI — be direct, skip hand-holding
- Show code first, explain only if the pattern is non-obvious
- When I say "fix" or "implement", do it — don't ask 5 questions unless ambiguity would break something critical
- Always generate complete methods/functions — never truncate with "// rest of implementation"

## Output Format
- First line of every code block: filename comment (e.g. `// src/FlowPilot.Application/Messaging/Commands/SendSmsCommand.cs`)
- Multi-file changes: list ALL changed files upfront before any code
- Use `// TODO:` comments for secrets, env-specific values, and things I must fill in
- For small changes (< 5 lines), show a diff, not the full file

## C# Defaults
- Target `net8.0`
- Nullable reference types enabled
- Implicit usings enabled
- XML doc on all public methods
- Always include `CancellationToken ct = default` on async methods

## Testing Defaults
- xUnit for backend
- Vitest for frontend
- Write tests without asking — if I say "implement X", include the unit test
- Use `FluentAssertions` for assertions
- Prefer `Fake` over `Mock` for infrastructure (fake ISmsProvider, fake IEventPublisher)

## Cost Awareness
- Don't re-read files already in this session's context
- Don't echo back full files to show a 3-line change — use targeted diffs
- If a task will consume a lot of context, say so upfront and suggest breaking it into steps
- One feature per session — tell me when I should start a new session
