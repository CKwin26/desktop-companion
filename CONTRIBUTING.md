# Contributing

## Scope

This repository is centered on the WPF desktop companion under `apps/desktop-shell-wpf`.

Before adding major functionality, keep these boundaries in mind:

- this is a desktop companion, not a full project-management app
- conversation-first UX takes priority over panel-heavy tooling
- local permissions and privacy must stay explicit
- legacy prototypes may be referenced, but new product work should land in the WPF host unless there is a strong reason not to

## Recommended Workflow

1. Open an issue or discussion for non-trivial product or architecture changes.
2. Keep changes scoped to one concern when possible.
3. Prefer small, reviewable pull requests.
4. Add or update docs when behavior changes.
5. Run the relevant local checks before opening a PR.

## Local Checks

### WPF host

```powershell
dotnet build DesktopCompanion.Windows.sln
```

### TypeScript workspace

```powershell
npm ci
npm run typecheck
```

## Code Guidelines

- Prefer clear, direct naming over framework cleverness.
- Keep the WPF host responsive; avoid blocking UI-thread work.
- Treat local file access as permission-gated behavior.
- Keep AI-provider logic replaceable.
- Do not silently expand the product into a heavy dashboard or admin tool.

## Pull Requests

Please include:

- what changed
- why it changed
- how it was validated
- any user-visible behavior change
- screenshots or short recordings for UI changes when useful
