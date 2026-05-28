# FoundryWebUI-X — Cross-Platform Migration Implementation Plan

This document is the authoritative implementation plan for converting the
Windows + IIS–only `FoundryWebUI` project into a cross-platform .NET 10 app
named **FoundryWebUI-X** that runs natively on **macOS (Apple Silicon)** and
**Windows (x64)**, with no IIS, no Windows Authentication, and no Windows-only
APIs in the runtime code path.

The plan is derived from a clarification dialogue with the project owner.
Decisions captured here override anything in the legacy `README.md` /
`DEPLOYMENT.md` until those files are rewritten as part of the migration.

---

## 1. Goals and non-goals

### Goals

- Run on **macOS (arm64)** and **Windows (x64)** with a single codebase.
- Target **.NET 10** with **C# 14**, broadly adopting modern language idioms.
- Drop all hard dependencies on **IIS**, **ASP.NET Core Module v2**,
  **Windows Authentication**, **System.Diagnostics.EventLog**, MSIX/WindowsApps
  lookup paths, and `C:\Users\*` enumeration.
- Preserve all current product features: chat (SSE), model browse/download/
  delete, system prompts, settings (Foundry cache directory), log viewing,
  Foundry Local connection auto-discovery.
- Replace IIS hosting with **Kestrel bound to loopback `127.0.0.1:5207`**.
- Auto-launch the user's default browser on startup (opt-out).
- Provide both **self-contained** and **framework-dependent** publish artifacts
  on tagged releases, attached to a GitHub Release.
- Add a comprehensive automated test suite (unit + integration + Playwright)
  using **TUnit** and **Imposter**, executed on **GitHub Actions** across
  ubuntu / macOS / Windows runners.

### Non-goals

- Linux runtime support (build-only on `ubuntu-latest` for CI speed).
- Service supervision (systemd, launchd, Windows Service). Manual launch only.
- Chat persistence, multi-user / session isolation, RAG, file upload, model
  parameter tuning UI — remain in the existing Roadmap, untouched.
- Backwards compatibility with the legacy IIS deployment (this is a one-shot
  conversion of a fork).
- Migration of legacy `system-prompts.json` placement (clean fork).

---

## 2. Naming and identity

| Item | Value |
|---|---|
| Repo / display name / README title | `FoundryWebUI-X` |
| Solution file | `FoundryWebUI-X.slnx` (new XML solution format) |
| Project file | `FoundryWebUI-X.csproj` |
| `<AssemblyName>` | `FoundryWebUI-X` |
| `<RootNamespace>` | `FoundryWebUI` (kept to minimize diff churn) |
| Code namespaces | `FoundryWebUI`, `FoundryWebUI.Services`, `FoundryWebUI.Endpoints`, `FoundryWebUI.Models` |
| User config directory (macOS) | `~/Library/Application Support/FoundryWebUI-X/` |
| User config directory (Windows) | `%LOCALAPPDATA%\FoundryWebUI-X\` |

The repo and the user-facing strings (window title, README, build artifacts,
launchctl/Service labels if ever added later) use `FoundryWebUI-X`. C#
identifiers remain `FoundryWebUI` because `-` is not a legal identifier
character; this keeps the existing namespace and minimizes per-file rewrites.

---

## 3. Runtime, language, and tooling baseline

- `<TargetFramework>net10.0</TargetFramework>`
- `<LangVersion>14</LangVersion>` (explicit)
- `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- `<AnalysisMode>Recommended</AnalysisMode>`, `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`
- `<EnableNETAnalyzers>true</EnableNETAnalyzers>`
- `<RootNamespace>FoundryWebUI</RootNamespace>`
- `<AssemblyName>FoundryWebUI-X</AssemblyName>`
- MinVer (`Minver` NuGet) for versioning from git tags
- Serilog for structured logging (replaces homegrown `InMemoryLoggerProvider`)
- `System.CommandLine` (stable in .NET 10) for CLI parsing
- `Microsoft.AspNetCore.HttpOverrides` — only if needed; reverse proxy support
  is **not** required (Kestrel loopback only)

### Broadly adopted C# 14 / modern idioms

- File-scoped namespaces
- Primary constructors on services and controllers
- Collection expressions (`[..]`)
- `field` keyword for backed auto-properties
- Target-typed `new`
- Pattern matching enhancements
- `using` declarations
- Raw string literals where helpful

Severity for these idiom-related analyzers: `warning` (becomes `error` in CI
via `TreatWarningsAsErrors`). Pure-noise style rules (trailing whitespace in
markdown, etc.) stay `suggestion`.

### `.editorconfig` (project-root)

A new `.editorconfig` enforces:

- 4-space indent for `.cs`, `.csproj`, `.slnx`, `.json`
- 2-space indent for `.yml`, `.yaml`, `.md`
- `csharp_style_namespace_declarations = file_scoped:warning`
- `csharp_style_prefer_primary_constructors = true:warning`
- `dotnet_style_collection_expression = true:warning`
- `csharp_style_var_for_built_in_types = true:warning`
- `csharp_style_var_when_type_is_apparent = true:warning`
- `csharp_style_var_elsewhere = true:warning`
- `dotnet_diagnostic.CA1416.severity = warning` (platform-compatibility)
- `dotnet_diagnostic.CS1591.severity = none` (missing XML docs — not required)
- `end_of_line = lf` for all source files; `crlf` for `*.ps1` only

---

## 4. Hosting and configuration changes

### Program.cs

Replace the existing minimal hosting with:

- Kestrel only, default URL `http://127.0.0.1:5207` (loopback).
- Serilog as the application logging backend, configured with three sinks:
  - **Console** sink (minimum `Information`)
  - **Rolling file** sink, daily rotation, 7-day retention, path
    `<userConfigDir>/logs/app-.log`
  - **In-memory** sink (custom `IBatchedLogEventSink` or `InMemorySink` from
    `Serilog.Sinks.InMemory`) — backs the `/api/logs/app` endpoint
- `System.CommandLine` for argument parsing. Recognized options:
  - `--host <hostname>` (default `127.0.0.1`)
  - `--port <int>` (default `5207`)
  - `--no-browser` (boolean; also via `FOUNDRYWEBUI_NO_BROWSER=1`)
  - `--config <path>` (alternate `appsettings.json` location)
- Browser auto-launch: after `app.StartAsync()` succeeds, open
  `http://127.0.0.1:<port>/` via:
  - Windows: `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`
  - macOS: `Process.Start("open", url)`
  - Suppressed when `--no-browser` is passed or `FOUNDRYWEBUI_NO_BROWSER=1`,
    or when stdin/stdout is redirected (basic test/CI detection).
- DI registrations carry forward: `FoundryLocalService`, sub-services
  (`EndpointDiscoveryService`, `ModelCatalogService`, `ChatStreamingService`,
  `ModelDownloadService`, `ModelDeletionService`), `SystemPromptStore`.

### appsettings.json

Trimmed:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft.AspNetCore": "Warning" }
    }
  },
  "AllowedHosts": "*",
  "LlmProviders": { "Foundry": { "Endpoint": "" } },
  "Foundry": { "ExecutablePath": "" }
}
```

The legacy top-level `FoundryExecutablePath` becomes `Foundry:ExecutablePath`
for grouping; old key is *not* migrated (clean fork).

### Files removed

- `web.config`
- `Install-FoundryWebUI.ps1`
- `Install-FoundryWebUI-Desktop.ps1`
- `DEPLOYMENT.md`

---

## 5. Cross-platform code changes

### 5.1 `Services/FoundryLocalService.cs`

- Replace `\\IIS app pool\\` and `icacls` text in error/log messages with
  generic "the FoundryWebUI-X process lacks write access to <path>" guidance.
- The chat fallback message "Check IIS stdout logs" becomes "Check application
  logs (Logs page)".
- Port probing already cross-platform; keep as-is.
- Add a `static class FoundryExecutable` helper (moved out of the controller).

### 5.2 `Endpoints/` (Minimal API groups, replacing `Controllers/ApiController.cs`)

- Drop `GetEventLogEntries` and the `eventlog` case in `/api/logs/{source}`.
- Replace `GetIisStdoutLogs` with `GetStdoutLogs` reading from the new Serilog
  rolling-file location under `<userConfigDir>/logs/`. Route becomes
  `/api/logs/stdout`.
- Replace `GetFoundryLogs` Windows path enumeration with a single cross-
  platform discovery: `Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".foundry", "logs")`.
- Replace `ResolveFoundryExecutable` Windows-only path search with the new
  cross-platform helper (see §5.3).
- Replace `ResolveFoundryHomeDirectory` and `ResolveFoundryConfigPath` with
  cross-platform variants that use `Environment.SpecialFolder.UserProfile`
  only (no `C:\Users\*` enumeration).
- Path separator: use `Path.PathSeparator` (`;` on Windows, `:` elsewhere)
  when splitting `PATH`.

### 5.3 New `Services/Platform/FoundryExecutable.cs`

A small static helper:

```csharp
public static string Resolve(IConfiguration config)
{
    // 1. Config override
    var configured = config["Foundry:ExecutablePath"];
    if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        return configured;

    var exeName = OperatingSystem.IsWindows() ? "foundry.exe" : "foundry";

    // 2. PATH search using Path.PathSeparator
    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                        .Split(Path.PathSeparator))
    {
        if (string.IsNullOrWhiteSpace(dir)) continue;
        var candidate = Path.Combine(dir.Trim(), exeName);
        if (File.Exists(candidate)) return candidate;
    }

    // 3. Platform conventional locations
    foreach (var candidate in PlatformCandidates(exeName))
        if (File.Exists(candidate)) return candidate;

    // 4. Fallback (let the OS try)
    return exeName;
}
```

`PlatformCandidates` returns:

- **macOS**: `/usr/local/bin/foundry`, `/opt/homebrew/bin/foundry`,
  `~/.foundry/bin/foundry`, `/Applications/Foundry Local.app/Contents/MacOS/foundry`
- **Windows**: `%ProgramFiles%\FoundryLocal\foundry.exe`,
  `%LOCALAPPDATA%\Programs\FoundryLocal\foundry.exe`, and the existing
  WindowsApps MSIX search retained as a last-resort fallback.

### 5.4 New `Services/Platform/UserPaths.cs`

```csharp
public static class UserPaths
{
    public static string ConfigDir => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData),
                       "FoundryWebUI-X")
        : Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile),
                       "Library", "Application Support", "FoundryWebUI-X");

    public static string LogsDir => Path.Combine(ConfigDir, "logs");
    public static string SystemPromptsFile => Path.Combine(ConfigDir, "system-prompts.json");
    public static string FoundryHome => Path.Combine(
        Environment.GetFolderPath(SpecialFolder.UserProfile), ".foundry");
}
```

`SystemPromptStore` is refactored to take `UserPaths.SystemPromptsFile`
instead of writing next to `ContentRoot`. The directory is created on first
write.

### 5.5 `Services/InMemoryLogStore.cs`

Deleted. Replaced by a Serilog in-memory sink read via a small
`IInMemoryLogReader` service. `/api/logs/app` returns the recent log buffer.

---

## 6. Logs page (UI) changes

`Pages/Logs.cshtml` + `wwwroot/js/logs.js`:

- **Remove** tabs: *IIS stdout*, *Windows Event Log*.
- **Keep** tab: *Application logs* (in-memory ring buffer / Serilog memory sink).
- **Add** tab: *App stdout* (reads Serilog rolling file).
- **Keep** tab: *Foundry Local logs* (file-based, cross-platform discovery).

Each tab continues to support filter + search + line-count selection. (The shipped implementation collapsed this to a single "Foundry Local" tab for simplicity.)

---

## 7. Settings page

`Pages/Settings.cshtml` + cache-directory editing:

- Keep the feature. The user can edit Foundry Local's cache directory via
  `foundry.config.json` (which the Foundry service reads on restart).
- Replace the `C:\Users\*` enumeration in `ResolveFoundryConfigPath` with a
  single check at `UserPaths.FoundryHome/foundry.config.json`.

---

## 8. Browser auto-launch

`Services/BrowserLauncher.cs`:

```csharp
public static void TryOpen(string url, ILogger logger)
{
    if (Environment.GetEnvironmentVariable("FOUNDRYWEBUI_NO_BROWSER") == "1")
        return;
    try
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
        else
            logger.LogInformation("Browser auto-launch skipped on this OS");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to auto-launch browser at {Url}", url);
    }
}
```

Wired via a hosted service that runs once after Kestrel reports its first
listening address.

---

## 9. Launch scripts

Two pairs:

### Development (runs from source)

- `scripts/dev.sh` (macOS, bash): `dotnet run --project FoundryWebUI-X.csproj -- "$@"`
- `scripts/dev.ps1` (Windows, PowerShell): `dotnet run --project FoundryWebUI-X.csproj -- @args`

### Production (runs published binary)

- `scripts/start.sh` (macOS): launches `./publish/FoundryWebUI-X` and forwards
  arguments
- `scripts/start.ps1` (Windows): launches `.\publish\FoundryWebUI-X.exe` and
  forwards arguments

Both pairs accept `--host`, `--port`, `--no-browser`, `--config`. README
documents each clearly with example invocations.

---

## 10. Solution and project layout

```
FoundryWebUI-X/
├── FoundryWebUI-X.slnx
├── FoundryWebUI-X.csproj
├── Program.cs
├── appsettings.json
├── .editorconfig
├── .gitignore
├── LICENSE.txt                  (MIT, unchanged)
├── README.md                    (rewritten)
├── docs/planning/
│   └── cross_platform_migration_implementation_plan.md  (this file)
├── Endpoints/
│   ├── StatusEndpoints.cs
│   ├── ModelsEndpoints.cs
│   ├── ChatEndpoints.cs
│   ├── LogsEndpoints.cs
│   ├── SettingsEndpoints.cs
│   ├── SystemPromptsEndpoints.cs
│   └── EndpointRegistry.cs
├── Models/
├── Pages/
├── Properties/
├── docs/
│   └── planning/
│       └── cross_platform_migration_implementation_plan.md  (this file)
├── Services/
│   ├── Platform/
│   │   ├── FoundryExecutable.cs
│   │   ├── UserPaths.cs
│   │   └── BrowserLauncher.cs
│   ├── FoundryLocalService.cs
│   ├── EndpointDiscoveryService.cs
│   ├── ModelCatalogService.cs
│   ├── ChatStreamingService.cs
│   ├── ModelDownloadService.cs
│   ├── ModelDeletionService.cs
│   └── SystemPromptStore.cs
├── wwwroot/
├── scripts/
│   ├── dev.sh
│   ├── dev.ps1
│   ├── start.sh
│   └── start.ps1
├── tests/
│   ├── FoundryWebUI-X.UnitTests/
│   │   └── FoundryWebUI-X.UnitTests.csproj
│   ├── FoundryWebUI-X.IntegrationTests/
│   │   └── FoundryWebUI-X.IntegrationTests.csproj
│   └── FoundryWebUI-X.E2ETests/
│       └── FoundryWebUI-X.E2ETests.csproj
└── .github/
    └── workflows/
        ├── ci.yml
        └── release.yml
```

The new `.slnx` lists the app project plus the three test projects.

---

## 11. Testing strategy

### Test framework: TUnit

Per <https://tunit.dev/docs/getting-started/installation>:

- Add the `TUnit` NuGet package only.
- **Do NOT** install `Microsoft.NET.Test.Sdk`, `xunit`, `nunit`, `MSTest`, or
  any other test platform; TUnit runs on Microsoft.Testing.Platform natively.
- Set `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`
  in each test csproj.
- Set `<OutputType>Exe</OutputType>` so the test project is its own runnable.
- Add `<IsTestProject>true</IsTestProject>`.

### Mocking: Imposter

- Imposter (<https://github.com/themidnightgospel/Imposter>) for service-level
  mocking.
- For `HttpClient` interception specifically, if Imposter cannot cleanly
  intercept `HttpMessageHandler` at implementation time, fall back to a small
  custom `TestHttpMessageHandler` (~30 LOC) for those specific tests. This
  fallback was pre-approved.

### Integration tests

- `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>` to host
  the app in-process.
- Foundry Local HTTP endpoints replaced via a captured `HttpMessageHandler`
  registered against the typed `HttpClient`.

### Playwright tests

- `Microsoft.Playwright` package. CI step runs `playwright install chromium`.
- Chromium only.
- App-under-test runs via a long-lived `WebApplicationFactory<Program>` that
  exposes a real Kestrel server on a random loopback port. A small in-process
  HTTP stub server (built with `WebApplication.CreateBuilder`) replaces
  Foundry Local at a configured `LlmProviders:Foundry:Endpoint`.
- `FOUNDRYWEBUI_NO_BROWSER=1` set for all test runs.

### Coverage targets

- `FoundryLocalService` — endpoint discovery, status parsing, catalog merge,
  chat SSE parsing, download progress, delete, cache directory.
- `SystemPromptStore` — CRUD + JSON persistence in the per-user dir
  (tests redirect via `UserPaths` test seam).
- Serilog in-memory sink — ring-buffer ordering and bounded size.
- `Endpoints` — all routes via `WebApplicationFactory`.
- `FoundryExecutable.Resolve` and `UserPaths` — platform branches via
  injectable platform detection.
- Playwright smoke set:
  - `/`, `/Models`, `/Logs`, `/Settings` render without console errors.
  - Chat: send a message → assert streamed tokens visible.
  - Models: list renders → "Download" triggers SSE progress UI → "Remove"
    succeeds.
  - Settings: cache-directory PUT round-trips; system-prompts CRUD round-trips.
  - Logs: *Foundry Local logs* tab populates; EventLog
    and IIS tabs are absent from the DOM.

### Coverage collection

- `coverlet.collector` referenced from each test project.
- CI produces a Cobertura XML + an HTML report as a build artifact.
- README displays a status-only coverage badge that links back to the latest
  CI run (no third-party service).

---

## 12. GitHub Actions

### `.github/workflows/ci.yml`

Triggers: `push` to `main`, `pull_request`.

Matrix: `os = [ubuntu-latest, macos-latest, windows-latest]`.

Jobs:

1. **build-and-test**
   - Checkout (with `fetch-depth: 0` for MinVer)
   - Setup .NET 10 SDK
   - `dotnet restore`
   - `dotnet format --verify-no-changes` (formatting gate)
   - `dotnet build --configuration Release --no-restore`
   - `pwsh -c "playwright install chromium"` (Microsoft.Playwright tool)
   - `dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage"`
     for each test project
   - Upload coverage and Playwright traces as workflow artifacts.
   - `FOUNDRYWEBUI_NO_BROWSER=1` env exported for the whole job.

### `.github/workflows/release.yml`

Triggers: `push` on tags matching `v*`, and `workflow_dispatch`.

Jobs:

1. **build-matrix** (matrix over RID + linkage):
   - `win-x64` self-contained
   - `win-x64` framework-dependent
   - `osx-arm64` self-contained
   - `osx-arm64` framework-dependent
   - Runs on the matching native runner (`windows-latest` for `win-x64`,
     `macos-latest` for `osx-arm64`) to avoid cross-compilation surprises.
   - Outputs to `artifacts/<rid>-<linkage>/`
   - Archives: `.zip` for Windows, `.tar.gz` for macOS.
   - Includes a `SHA256SUMS.txt` per artifact.
2. **release**
   - `softprops/action-gh-release@v2` creates / updates a GitHub Release for
     the tag and attaches all artifacts plus checksums.

---

## 13. Documentation

### README.md (rewritten)

Sections:

1. Header (badges: build, license, .NET 10, coverage)
2. What it is, with fork attribution:
   > Forked from [itopstalk/FoundryWebUI](https://github.com/itopstalk/FoundryWebUI) — cross-platform variant for macOS (Apple Silicon) and Windows (x64), without IIS.
3. Requirements (Foundry Local GA — links to
   <https://devblogs.microsoft.com/foundry/foundry-local-ga/> and
   <https://github.com/microsoft/Foundry-Local>)
4. Quick start
   - Install Foundry Local (Windows: `winget install Microsoft.FoundryLocal`,
     macOS: per the official Microsoft Foundry-Local repo's release page)
   - Install .NET 10 SDK (for source build) or runtime (for framework-dep
     publish)
   - Clone, then `./scripts/dev.sh` (macOS) or `.\scripts\dev.ps1` (Windows)
   - Browser auto-launches to `http://127.0.0.1:5207/`
5. CLI flags: `--host`, `--port`, `--no-browser`, `--config`
6. Running from a published binary
7. Features (carried forward from old README, IIS bullet removed)
8. Architecture diagram (Kestrel + Foundry Local)
9. API endpoints (with the `/api/logs/eventlog` row removed and
   `/api/logs/stdout` row added)
10. Configuration (`appsettings.json` keys, env-var equivalents)
11. Per-user data locations (settings + logs)
12. Building and testing
13. Releases (artifacts table)
14. Troubleshooting (rewritten; IIS-specific rows removed)
15. Roadmap (unchanged)
16. License (MIT, unchanged) + fork attribution restated

### DEPLOYMENT.md

Deleted.

---

## 14. Execution order

1. **Plan committed** — this document.
2. **Project metadata** — rename csproj, bump TFM, set lang version, analyzers,
   `.editorconfig`, MinVer, Serilog, System.CommandLine.
3. **File deletions** — `web.config`, both PS installers, `DEPLOYMENT.md`,
   `Services/InMemoryLogStore.cs`.
4. **Platform helpers** — `Services/Platform/UserPaths.cs`,
   `Services/Platform/FoundryExecutable.cs`,
   `Services/Platform/BrowserLauncher.cs`.
5. **Service refactors** — `SystemPromptStore`, `FoundryLocalService`,
   `ApiController` for the Logs and Settings routes.
6. **UI updates** — `Pages/Logs.cshtml`, `wwwroot/js/logs.js` to drop EventLog
   and IIS tabs and add the App-stdout tab.
7. **Hosting** — rewrite `Program.cs` with Kestrel, Serilog, System.CommandLine,
   browser auto-launch.
8. **Scripts** — four launch scripts under `scripts/`.
9. **Solution** — create `FoundryWebUI-X.slnx`.
10. **Tests** — create the three test projects with TUnit + Imposter +
    Playwright. Implement coverage targets listed in §11.
11. **CI** — add `.github/workflows/ci.yml` and `release.yml`.
12. **README rewrite** — replace the entire `README.md` content per §13.
13. **Local verification** — `dotnet build`, `dotnet test`, sample
    `dotnet run`, confirm browser launches and `/api/status` reachable
    (without a running Foundry Local, expect `IsAvailable: false`).
14. **Final cleanup** — confirm no remaining references to IIS, EventLog,
    WindowsApps, `C:\Users\Administrator`, app-pool, or `icacls`.

---

## 15. Acceptance checks (definition of done)

- `dotnet build` succeeds with `TreatWarningsAsErrors=true` on net10.0.
- `dotnet format --verify-no-changes` exits 0.
- `dotnet test` passes for all three test projects on macOS and Windows.
- `grep -rEi "IIS|EventLog|WindowsApps|C:\\\\Users\\\\Administrator|app[- ]?pool|icacls" --include='*.cs' --include='*.cshtml' --include='*.js' --include='*.json'` returns no matches in runtime code (matches only in this plan and historical files are acceptable until removal).
- README contains no references to IIS / Windows Authentication / Hosting
  Bundle / `web.config` / `Install-FoundryWebUI*.ps1`.
- Launching `./scripts/dev.sh` (or `.\scripts\dev.ps1`) prints a startup
  banner, opens a browser to `http://127.0.0.1:5207/`, and the page renders.
- `--no-browser` and `FOUNDRYWEBUI_NO_BROWSER=1` both suppress the browser.
- A `git tag v0.1.0 && git push --tags` (when the user chooses to do so)
  would trigger the release workflow and produce four artifacts plus
  checksums attached to a new GitHub Release. (CI verification only; tagging
  is the user's call.)
