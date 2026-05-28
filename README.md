# FoundryWebUI-X

Cross-platform web UI for **[Microsoft Foundry Local](https://github.com/microsoft/Foundry-Local)** — a self-hosted, ASP.NET Core chat interface that runs as a local desktop service on **macOS (Apple Silicon)** and **Windows (x64)**, without IIS or Windows Authentication.

> Forked from [itopstalk/FoundryWebUI](https://github.com/itopstalk/FoundryWebUI). This variant drops the IIS/Windows-Server hosting model in favor of a Kestrel-only, loopback-by-default local app that runs identically on macOS and Windows. It supports **Foundry Local only**.

[![CI](https://github.com/cbgrasshopper/foundrywebui-crossplatform/actions/workflows/ci.yml/badge.svg)](https://github.com/cbgrasshopper/foundrywebui-crossplatform/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)
![.NET 10](https://img.shields.io/badge/.NET-10-purple)
![C# 14](https://img.shields.io/badge/C%23-14-blueviolet)

---

## What it is

A small, single-process ASP.NET Core app that:

- Auto-detects (or is pointed at) a running **Foundry Local** instance.
- Provides a chat UI with streaming SSE responses.
- Lets you browse the Foundry catalog, download / remove models, and manage system prompts.
- Surfaces Foundry Local logs in-browser — no EventLog, no IIS, no app-stdout tab.

It is intended to be launched manually (script or binary) on a developer's machine. It does **not** install itself as a service, configure auto-start, or open public network ports.

## Requirements

| | |
|---|---|
| OS | **macOS (arm64)** or **Windows (x64)** |
| .NET | **.NET 10 SDK** (to build from source) or .NET 10 Runtime (framework-dependent published binary) |
| Foundry Local | Foundry Local **GA** — see [GA announcement](https://devblogs.microsoft.com/foundry/foundry-local-ga/) and the [official repo](https://github.com/microsoft/Foundry-Local) |

If Foundry Local is missing, FoundryWebUI-X surfaces an in-UI hint with install instructions:

- **Windows**: `winget install Microsoft.FoundryLocal`
- **macOS**: follow the official Foundry-Local release page on GitHub

## Quick start (from source)

```bash
# 1. Clone
git clone https://github.com/cbgrasshopper/foundrywebui-crossplatform.git
cd foundrywebui-crossplatform

# 2. Start Foundry Local (in another terminal)
foundry service start

# 3. Launch FoundryWebUI-X
./scripts/dev.sh         # macOS / Linux
.\scripts\dev.ps1        # Windows (PowerShell)
```

The app binds to **`http://127.0.0.1:5207/`** (loopback only) and auto-opens your default browser.

## Quick start (from a published binary)

After publishing once with `dotnet publish` (see [Releases](#releases)):

```bash
./scripts/start.sh       # macOS / Linux
.\scripts\start.ps1      # Windows
```

## CLI flags

All four launch scripts forward arguments to `FoundryWebUI-X`:

| Flag | Default | Description |
|---|---|---|
| `--host <addr>` | `127.0.0.1` | Bind address. Loopback-only by default. |
| `--port <n>` | `5207` | TCP port. |
| `--no-browser` | *(off)* | Suppress browser auto-launch. Same as `FOUNDRYWEBUI_NO_BROWSER=1`. |
| `--config <file>` | *(none)* | Additional `appsettings.json`-style override file. |

Example:

```bash
./scripts/dev.sh -- --port 8080 --no-browser
```

## Features

- **Chat** — streaming SSE responses, message history, basic Markdown rendering.
- **Model management** — browse the full Foundry catalog, download with live progress, remove cached models.
- **Sortable models table** with “Can Run” RAM estimates.
- **System prompt library** — create, edit, delete reusable prompts.
- **Logs page** — single **Foundry Local** tab (reads Foundry's log dir cross-platform).
- **Auto-discovery** of the Foundry Local endpoint via local port scan.
- **REST-only** — no CLI dependency; uses Foundry Local REST APIs directly.
- **Dark theme** Bootstrap 5 UI.

## Architecture

```diagram
╭─────────╮     HTTP/SSE      ╭───────────╮     HTTP      ╭───────────────╮
│ Browser │ ─────────────────▶│  Kestrel  │ ────────────▶│ Foundry Local │
╰─────────╯                   │ ASP.NET   │              ╰───────────────╯
                              │ Core app  │
                              ╰───────────╯
                              ▲          │
                              │          ▼
                       ╭────────────────────╮
                       │ Per-user data dirs │
                       │  config / logs     │
                       ╰────────────────────╯
```

| Component | Role |
|---|---|
| **Kestrel** | HTTP host, bound to loopback by default. No reverse proxy. |
| **Razor Pages** | `/` (Chat), `/Models`, `/Logs`, `/Settings` |
| **`ApiController`** | REST + SSE endpoints under `/api/` |
| **`FoundryLocalService`** | Adapter for the Foundry Local REST API |
| **`SystemPromptStore`** | JSON-backed prompt library in the per-user config dir |
| **Serilog** | Console + rolling-file + bounded in-memory sinks |
| **`BrowserLauncher`** | Cross-platform browser opener (`open` / `cmd /c start`) |

## API endpoints

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/status` | Provider health check |
| `GET` | `/api/system-info` | System RAM info (for "Can Run") |
| `GET` | `/api/models` | List models (catalog + loaded) |
| `GET` | `/api/models/loaded` | List currently loaded models |
| `POST` | `/api/chat?provider=foundry` | Streaming chat (SSE) |
| `POST` | `/api/models/download` | Download a model (SSE progress) |
| `DELETE` | `/api/models/{id}` | Remove a cached model |
| `POST` | `/api/reconnect` | Re-discover Foundry Local endpoint |
| `GET` | `/api/logs/app` | In-memory application logs |
| `GET` | `/api/logs/stdout` | Rolling app log file (own stdout) |
| `GET` | `/api/logs/foundry` | Foundry Local log files |
| `GET` | `/api/system-prompts` | List system prompts |
| `POST` | `/api/system-prompts` | Create a prompt |
| `PUT` | `/api/system-prompts/{id}` | Update a prompt |
| `DELETE` | `/api/system-prompts/{id}` | Delete a prompt |

## Configuration

Edit `appsettings.json` (or pass an override file via `--config`):

```json
{
  "LlmProviders": {
    "Foundry": {
      "Endpoint": ""
    }
  },
  "Foundry": {
    "ExecutablePath": ""
  }
}
```

| Setting | Default | Notes |
|---|---|---|
| `LlmProviders:Foundry:Endpoint` | *(blank — auto-detect)* | Set to `http://localhost:5273` to skip port scanning. |
| `Foundry:ExecutablePath` | *(blank — discovered)* | Absolute path to the `foundry` binary. |
| `FOUNDRYWEBUI_NO_BROWSER` (env) | *(unset)* | If `1`, suppress browser auto-launch. |

## Per-user data locations

| | macOS | Windows |
|---|---|---|
| Settings (`system-prompts.json`) | `~/Library/Application Support/FoundryWebUI-X/` | `%APPDATA%\FoundryWebUI-X\` |
| Logs (`app-YYYYMMDD.log`) | `~/Library/Logs/FoundryWebUI-X/` | `%LOCALAPPDATA%\FoundryWebUI-X\logs\` |

These directories are created on first launch.

## Building and testing

```bash
# Build
dotnet build FoundryWebUI-X.csproj -c Release

# Unit tests
dotnet run --project tests/FoundryWebUI-X.UnitTests/FoundryWebUI-X.UnitTests.csproj -c Release

# Integration tests (in-memory TestServer + stubbed Foundry handler)
dotnet run --project tests/FoundryWebUI-X.IntegrationTests/FoundryWebUI-X.IntegrationTests.csproj -c Release

# E2E tests (Playwright, Chromium only; downloads browser on first run)
dotnet run --project tests/FoundryWebUI-X.E2ETests/FoundryWebUI-X.E2ETests.csproj -c Release
```

Test stack:

- **[TUnit](https://tunit.dev)** — execution engine for all three projects. Do **not** add `Microsoft.NET.Test.Sdk`, xUnit, NUnit, MSTest, or coverlet.
- **[Imposter](https://themidnightgospel.github.io/Imposter/latest/)** — interface mocks where useful.
- **`WebApplicationFactory<Program>`** — integration tests via in-memory TestServer.
- **Microsoft.Playwright** (Chromium) — E2E smoke tests over a real Kestrel host with a stubbed Foundry server.

## Releases

CI builds publish artifacts for both supported RIDs in two flavors. The release workflow is triggered by a tag matching `v*` (or `workflow_dispatch`) and uploads to a GitHub Release.

| RID | Linkage | Archive | Single-file? |
|---|---|---|---|
| `win-x64` | self-contained | `.zip` | yes |
| `win-x64` | framework-dependent | `.zip` | yes |
| `osx-arm64` | self-contained | `.tar.gz` | yes |
| `osx-arm64` | framework-dependent | `.tar.gz` | yes |

Each archive ships alongside a `*.sha256` checksum file. Versions are computed by **[MinVer](https://github.com/adamralph/minver)** from the latest `v*` git tag, so `git tag v0.2.0 && git push --tags` is sufficient to cut a release.

To publish locally:

```bash
# macOS, self-contained, single-file
dotnet publish FoundryWebUI-X.csproj -c Release -r osx-arm64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish

# Windows, framework-dependent, single-file
dotnet publish FoundryWebUI-X.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true `
  -o publish
```

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Foundry status indicator is red | Foundry Local not running | `foundry service start`, then click 🔄 Reconnect |
| No models listed | Auto-discovery failed | Set `LlmProviders:Foundry:Endpoint` explicitly |
| Browser doesn't open | Headless terminal / `--no-browser` set | Open `http://127.0.0.1:5207/` manually |
| Port 5207 already in use | Conflicting local service | Pass `--port 8080` (or any free port) |
| `dotnet` not found | .NET 10 SDK not installed | Install from <https://dotnet.microsoft.com/download/dotnet/10.0> |
| Logs page empty | Just started — buffer is empty | Trigger some traffic (refresh chat, hit Models); logs will populate |

## Roadmap

- Conversation persistence (save / load chat history)
- Model parameter tuning (temperature, top_p, max_tokens)
- Multi-user session isolation
- File upload / document Q&A
- RAG integration

## License

MIT — see [LICENSE.txt](LICENSE.txt).

This project is a fork of [itopstalk/FoundryWebUI](https://github.com/itopstalk/FoundryWebUI) (also MIT-licensed).
