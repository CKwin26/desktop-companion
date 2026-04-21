# Desktop Companion

<p align="center">
  <img src="./docs/assets/github-cover.svg" alt="Desktop Companion cover" width="100%" />
</p>

Desktop Companion is a Windows-first desktop pet that acts like a conversational work companion.

The current canonical runtime is a WPF host under `apps/desktop-shell-wpf`. Earlier prototypes are still kept in the repo as references, but they are not the main product path anymore.

## What This Project Is

- A desktop pet that stays on the screen instead of behaving like a normal app window
- A conversation-first companion that can help untangle mixed work items
- A lightweight task and project-memory layer, not a full project-management suite
- A local-first assistant that can read project folders only after explicit permission
- A bridge that can hand work to VS Code / Codex and report back in the pet UI

## Current Product Direction

The project is intentionally scoped around one core idea:

> a desktop-resident companion that helps the user restart action

That means:

- conversation comes before task capture
- task memory stays lightweight
- project cognition is background support, not a dashboard product
- the pet shell matters as much as the reasoning layer

## Current Main Features

- WPF desktop pet host with transparent always-on-top shell
- single-click open/close interaction with bubble-based UI
- OpenAI-backed conversation support
- Ollama-backed local conversation support
- lightweight task memory and project-memory models
- project-dump digestion for mixed lists of work items
- explicit permission flow before scanning local project folders
- workspace ingestion for `README`, summary docs, notes, code, and selected text assets
- VS Code / Codex bridge for opening a workspace and dispatching structured tasks

## Visual Preview

<p align="center">
  <img src="./apps/desktop-shell-wpf/Assets/Images/tuanzi-avatar.png" alt="Tuanzi avatar" width="260" />
</p>

<p align="center">
  <img src="./docs/assets/companion-stack.svg" alt="Desktop Companion stack" width="100%" />
</p>

## Repository Layout

```text
.
├─ apps/
│  ├─ desktop-shell/          # legacy Tauri prototype
│  └─ desktop-shell-wpf/      # current canonical Windows host
├─ docs/                      # product, cognition, and architecture docs
├─ packages/                  # earlier TypeScript workspace packages kept for reference
├─ index.html                # earliest static prototype
├─ app.js
└─ styles.css
```

## Canonical Runtime

The active product runtime is:

- `DesktopCompanion.Windows.sln`
- `apps/desktop-shell-wpf/DesktopCompanion.WpfHost.csproj`

The WPF host currently owns:

- pet shell
- bubble UI
- conversation loop
- permissions
- local memory stores
- project cognition
- workspace ingestion
- OpenAI / Ollama providers
- VS Code / Codex bridge

## Legacy Areas

These parts remain in the repo as historical prototypes or reference implementations:

- `apps/desktop-shell` for the Tauri prototype
- `packages/*` for the earlier TypeScript workspace split
- root `index.html`, `app.js`, and `styles.css` for the earliest local prototype

They are useful as reference material, but they are not the current shipping path.

## Getting Started

### Requirements

- Windows
- .NET 8 SDK
- Node.js 20+ and npm
- optional: Ollama running locally on `http://127.0.0.1:11434`
- optional: VS Code and `codex` available on `PATH`

### Build the WPF Host

```powershell
dotnet build DesktopCompanion.Windows.sln
dotnet run --project apps/desktop-shell-wpf/DesktopCompanion.WpfHost.csproj
```

### Optional TypeScript Workspace Checks

```powershell
npm ci
npm run typecheck
```

## Configuration

### OpenAI

Set these environment variables if you want the pet to use OpenAI:

```powershell
$env:OPENAI_API_KEY="your-key"
$env:OPENAI_MODEL="gpt-5"
```

Optional:

```powershell
$env:OPENAI_BASE_URL="https://api.openai.com/v1/"
```

### Ollama

The local provider expects Ollama at:

- `http://127.0.0.1:11434`

The current default model in code is `gemma4:e4b`.

## Privacy and Permissions

- local folder reading is opt-in
- the app asks for permission before scanning project directories
- authorized workspace paths can be cleared from the app
- local memory is stored on the machine, not in the repo

## Documentation

- [Final product structure](./docs/final-product-structure.md)
- [Repo structure](./docs/repo-structure.md)
- [WPF host route](./docs/wpf-host-route.md)
- [Project cognition](./docs/project-cognition.md)
- [Tuanzi cognition assets](./docs/tuanzi-cognition-assets.md)
- [Work progress distillation report](./docs/work-progress-distillation-report.md)

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

This repository is currently prepared with the [MIT License](./LICENSE).
