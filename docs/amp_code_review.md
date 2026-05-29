# Code Review & Remediation Plan: FoundryWebUI-X

> Review date: 2026-05-29
> Reviewer: Amp (Sonnet 4.5)
> Repository: https://github.com/cbgrasshopper/foundrywebui-crossplatform
> Scope: full repository — source, tests, build, CI, docs, scripts, frontend.

---

## Executive summary

The project is small (~2,600 lines of C#, ~2,000 lines of JS), cleanly laid out, and the .NET surface is mostly idiomatic ASP.NET Core 10. The pain points cluster around:

1. A handful of stale/misleading statements in `README.md` that contradict the code.
2. Two long, under-tested methods doing too much (`ChatStreamingService.StreamChatAsync`, `ModelDeletionService.DeleteModelAsync`).
3. Shallow E2E coverage — Playwright tests only verify pages return `200`, never an interaction.
4. Residue from the upstream `FoundryWebUI` fork still visible in models, endpoints, and comments.
5. A handful of small structural quirks (1-method pass-through facades, leaked `HttpClient`, hard-coded fallback ports, hard-coded UI version).

None of this is catastrophic; the code is shippable. But the doc/code drift and the missing tests around the riskiest filesystem and process-launch code carry real risk of regressions slipping past CI.

---

## Table of contents

1. [Documentation accuracy](#1--documentation-accuracy)
2. [Technical debt](#2--technical-debt)
3. [Test value & comprehensiveness](#3--test-value--comprehensiveness)
4. [Code structure & maintainability](#4--code-structure--maintainability)
5. [CI / release pipeline](#5--ci--release-pipeline)
6. [Small bugs & smells](#6--small-bugs--smells)
7. [Implementation plan](#7--implementation-plan)

---

## 1 — Documentation accuracy

### `[High]` macOS logs path is wrong

- **README** ([`README.md#L169-L170`](../README.md)) claims:
  - macOS logs → `~/Library/Logs/FoundryWebUI-X/`
- **Code** ([`Services/Platform/UserPaths.cs#L29`](../Services/Platform/UserPaths.cs)) actually writes them to:
  - `~/Library/Application Support/FoundryWebUI-X/logs/`
- Either is reasonable; pick one and align both. Apple's HIG prefers `~/Library/Logs/<bundle>`, but the current code's "everything under one app dir" approach is simpler. Recommend: keep code as-is, fix README.

### `[High]` "REST-only — no CLI dependency" is incorrect

- **README Features** ([`README.md#L89`](../README.md)) claims:
  - "REST-only — no CLI dependency; uses Foundry Local REST APIs directly."
- **Reality**:
  - [`EndpointDiscoveryService.TryDiscoverViaCliAsync`](../Services/EndpointDiscoveryService.cs) shells out to `foundry service status` as a fallback.
  - [`StatusEndpoints.StartFoundry`](../Endpoints/StatusEndpoints.cs) shells out to `foundry service start` when the user clicks "Start Foundry".
- Reword to "REST-first; the Foundry CLI is invoked only as a fallback for endpoint discovery and to start the service from the UI."

### `[Med]` Imposter is advertised but unused

- README ([Test stack](../README.md#L192)) lists `Imposter` for "interface mocks where useful," but no test in the repo references it (`rg Imposter tests/` → 0 hits). All HTTP stubbing goes through [`TestHttpMessageHandler`](../tests/FoundryWebUI-X.TestInfrastructure/TestHttpMessageHandler.cs).
- Decide: drop the mention, or actually add Imposter and use it once.

### `[Med]` Hard-coded sidebar version

- [`Pages/Shared/_Layout.cshtml#L74`](../Pages/Shared/_Layout.cshtml): `<div class="sidebar-footer">v1.0</div>`
- MinVer is configured in [`FoundryWebUI-X.csproj#L62-L66`](../FoundryWebUI-X.csproj) and produces the real informational version at build time. Inject it.

### `[Low]` Architecture box vs. service list

- The [architecture diagram](../README.md#L94-L106) shows a single ASP.NET Core box; the next table lists five sub-services. Add one sentence noting the box hides those five.

### `[Low]` `--config` semantics under-specified

- README documents `--config <file>` as "Additional `appsettings.json`-style override file." [`Program.cs#L85`](../Program.cs) loads it with `optional: false` — so passing a missing path is a hard failure. Document the failure mode.

### `[Low]` csproj `NoWarn` comment drift

- [`FoundryWebUI-X.csproj#L16-L40`](../FoundryWebUI-X.csproj) has a long suppression-rationale comment, but `CA1305` is included in `NoWarn` without an entry in the comment. Re-sync.

### `[Low]` Duplicate review file

- [`docs/deepseek_code_review.md`](deepseek_code_review.md) covers similar ground. Either mark it as historical (date-stamp it in the title) or remove it to avoid two sources of truth.

---

## 2 — Technical debt

### `[High]` `ModelDeletionService.DeleteModelAsync` is fragile, untested

[`Services/ModelDeletionService.cs#L18-L148`](../Services/ModelDeletionService.cs)

- 130-line method reaching into Foundry's on-disk model cache directly.
- Two ad-hoc directory-matching passes: exact `Replace(':','-')`, then a "starts-with version-stripped" fallback.
- Mixes: HTTP unload call, HTTP status call to discover cache dir, directory enumeration for log purposes, exact-match deletion, fuzzy fallback deletion.
- **Zero unit tests**.
- Platform-specific: relies on Foundry's on-disk layout under a publisher subdirectory.

This is the single riskiest file in the repo. If Foundry changes their cache layout, deletion silently breaks and the user only sees a generic "Failed to remove model" toast.

**Recommendation:** Extract the on-disk matching into a pure helper (input: cache dir + model id; output: candidate path or null) and unit-test it against fake directory trees. Keep the HTTP unload + delete orchestration in the service.

### `[High]` `ChatStreamingService.StreamChatAsync` does too much

[`Services/ChatStreamingService.cs#L22-L201`](../Services/ChatStreamingService.cs)

- 180 lines in a single async iterator.
- Mixes: model loading, request payload building, SSE line parsing, JSON parsing, error object inspection, `[DONE]` handling, delta-vs-message branch, "no content" warning text, connection-closed fallback.
- Hard to test parsing in isolation because everything is interleaved with the HTTP stream.

**Recommendation:** Extract an `SseEventParser` (yields `(eventType, jsonPayload)` from a `Stream`) and a `ChatErrorMapper` (takes a `JsonElement`, returns a user-facing message). The iterator collapses to ~60 lines.

### `[High]` `ModelDownloadService.DownloadModelAsync` is over-engineered

[`Services/ModelDownloadService.cs#L26-L181`](../Services/ModelDownloadService.cs)

- The consumer already iterates with `await foreach`. There's no reason for the inner work to run on `Task.Run(...)` and write to a `Channel<DownloadProgress>` only for the iterator to read from the same channel.
- The pattern also silently swallows `OperationCanceledException` at line 164, so cancellations look like premature normal termination.

**Recommendation:** `yield return` directly from the read loop. Removes ~60 lines and ~one moving part.

### `[Med]` Endpoint handlers do too much I/O

- [`Endpoints/SettingsEndpoints.cs#L35-L123`](../Endpoints/SettingsEndpoints.cs) — `SetCacheDirectory` is ~90 lines of file I/O + JSON re-shaping inline.
- [`Endpoints/StatusEndpoints.cs#L44-L114`](../Endpoints/StatusEndpoints.cs) — `StartFoundry` launches a process, waits for exit, then polls for status.
- Both should move into services so they're (a) DI-testable and (b) not entangled with `IResult` shape.

### `[Med]` `EndpointDiscoveryService.HttpClient` is leaked

[`Services/EndpointDiscoveryService.cs#L27`](../Services/EndpointDiscoveryService.cs)

- The typed `HttpClient` is exposed as a public property and reused by every sibling service.
- Side effect: the global 2-hour `Timeout` applied for downloads ([`Program.cs#L170`](../Program.cs)) also applies to 5-second status probes (mitigated only by per-call `CancellationTokenSource`).
- Services that need an `HttpClient` should each get their own typed client via `IHttpClientFactory`.

### `[Med]` Magic-number fallback endpoint

- [`Services/EndpointDiscoveryService.cs#L102`](../Services/EndpointDiscoveryService.cs): falls back to `http://localhost:5272`.
- Undocumented, inconsistent with the app's own default port `5207`. Constantize and document, or return `null` to force an explicit error.

### `[Med]` Fork residue in models

[`Models/LlmModels.cs`](../Models/LlmModels.cs)

- Line 35: `// "foundry" or "ollama"` — Ollama is no longer supported.
- Line 47: `DownloadRequest.Provider` field — unused.
- Line 15: `ChatRequest.Stream` field — always implicitly `true`, never read.

Remove these or reintroduce intentional support.

### `[Med]` `LogsEndpoints` exposes dead sources

[`Endpoints/LogsEndpoints.cs#L22-L28`](../Endpoints/LogsEndpoints.cs)

- API supports `app`, `stdout`, `foundry`.
- The Logs page intentionally has only the `foundry` tab (asserted by [`EndpointTests.LogsPage_HasNoEventLogOrIisTabs`](../tests/FoundryWebUI-X.IntegrationTests/EndpointTests.cs#L178-L192) and by [`SmokeTests.LogsPage_HasExpectedTabsOnly`](../tests/FoundryWebUI-X.E2ETests/SmokeTests.cs#L46-L57)).
- `app` and `stdout` are reachable by hand but not via UI. Either remove from the API or surface in the UI.

### `[Low]` Pass-through facades

- [`Services/InMemoryLogReader.cs`](../Services/InMemoryLogReader.cs) — 20 lines, one method, delegates straight to `InMemoryLogSink.Snapshot`. Inline.
- [`Services/FoundryLocalService.cs`](../Services/FoundryLocalService.cs) — pure facade, no behavior. Endpoints already reach past it (e.g. `ModelsEndpoints` uses `provider.GetLoadedModelsAsync` *and* directly relies on `provider.GetAvailableModelsAsync`). Either: keep it and route everything through it, or remove and inject the five concrete services. Also not `sealed`.

### `[Low]` Empty page models

- `Pages/{Logs,Models,Settings}.cshtml.cs` are 8-line empty `PageModel` subclasses. Removable when no codebehind is needed (Razor will still bind `@page`).

### `[Low]` Unused `_logger`

- [`Pages/Index.cshtml.cs#L7-L11`](../Pages/Index.cshtml.cs) — injected logger never used.

### `[Low]` Primary-constructor style not enforced

- [`.editorconfig#L32`](../.editorconfig) sets `csharp_style_prefer_primary_constructors = true:warning`, but most services still use explicit constructors. Either downgrade severity or convert.

### `[Low]` jQuery shipped, unused

- [`Pages/Shared/_Layout.cshtml#L82`](../Pages/Shared/_Layout.cshtml) loads jQuery. Bootstrap 5 doesn't need it and the app's own JS uses vanilla DOM APIs. Drop the script.

---

## 3 — Test value & comprehensiveness

### Strengths

- `SystemPromptStore`, `InMemoryLogSink`, and `ModelDownloadService` have solid unit coverage that exercises both happy and error paths.
- `EndpointTests` covers the full HTTP pipeline through `WebApplicationFactory<Program>` against a stubbed Foundry — exactly the right middle layer.
- `BuildApp` is exposed for testing ([`Program.cs#L110`](../Program.cs)). Good seam.
- The TUnit-only stack with shared `Directory.Build.props` is clean and avoids the VSTest tax.

### `[High]` Zero tests for `ModelDeletionService`

Riskiest filesystem code in the codebase, completely uncovered. See §2.

### `[High]` Zero tests for `EndpointDiscoveryService` discovery cascade

The discovery state machine (config → cache → port-from-config → port-from-logs → CLI → fallback) is the most subtle logic in the app and has no direct tests. The integration tests only exercise it indirectly when the endpoint is hard-coded in the config.

### `[High]` `DownloadModelAsync_HandlesCancellation` asserts nothing

[`tests/FoundryWebUI-X.UnitTests/ModelDownloadServiceTests.cs#L213-L233`](../tests/FoundryWebUI-X.UnitTests/ModelDownloadServiceTests.cs)

```csharp
try
{
    await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini", cts.Token))
    {
    }
}
catch (OperationCanceledException)
{
    // Expected when the cancellation fires during streaming.
}
```

No `Assert`. Test passes whether cancellation happens, doesn't happen, or whether the entire method silently no-ops.

### `[Med]` E2E coverage is shallow

[`tests/FoundryWebUI-X.E2ETests/SmokeTests.cs`](../tests/FoundryWebUI-X.E2ETests/SmokeTests.cs) — 5 tests, all "page returns 200 + DOM has expected element". Zero interaction tests. The most bug-prone code in the app (`wwwroot/js/chat.js`, 792 lines) is never executed by any test.

### `[Med]` Notable gaps

| Area | Missing |
|---|---|
| `BrowserLauncher` | `ShouldLaunch` branches (env, redirect, options) |
| `ContextWindowLookup` | Loading, missing-file, malformed-JSON |
| `LogsEndpoints` | File-read path (only the `app` source is touched by integration tests) |
| `ChatStreamingService` | `connection_closed` branch, model-load failure branch |
| `Endpoints/SettingsEndpoints` | `SetCacheDirectory` happy and error paths |
| `Endpoints/StatusEndpoints` | `StartFoundry` exit-code mapping |

### `[Med]` Analyzers disabled in tests

[`tests/Directory.Build.props#L22`](../tests/Directory.Build.props): `<EnableNETAnalyzers>false</EnableNETAnalyzers>`. Tests are exactly where threading and disposal bugs hide; analyzers should run there too (with the existing `NoWarn` for test-friendly rules).

### `[Low]` `TestHttpMessageHandler.When` matches by substring

[`tests/FoundryWebUI-X.TestInfrastructure/TestHttpMessageHandler.cs#L23`](../tests/FoundryWebUI-X.TestInfrastructure/TestHttpMessageHandler.cs): `uri.Contains(pathOrUrl, OrdinalIgnoreCase)`. A stub for `/openai/unload/` also satisfies `/openai/unload/x`. Surprising; should at minimum match path segments.

---

## 4 — Code structure & maintainability

### `[High]` `wwwroot/js/chat.js` is 792 lines of un-moduled DOM code

Globals-as-state, inline `escHtml`, inline SSE parsing. Not exercised by any test. Either:

- Split into ES modules (`chat/state.js`, `chat/dropdown.js`, `chat/sse.js`), or
- Add Playwright tests that drive the actual chat flow, or
- Both.

Today this is the single largest piece of untested logic in the codebase.

### `[Med]` Duplicated `WriteSSE` and `JsonSerializerOptions`

- [`ChatEndpoints.cs#L53`](../Endpoints/ChatEndpoints.cs) and [`ModelsEndpoints.cs#L151`](../Endpoints/ModelsEndpoints.cs) define identical `WriteSSE` helpers.
- Three endpoint files instantiate their own `JsonSerializerOptions { PropertyNamingPolicy = CamelCase }`.
- Extract a shared helper (e.g. `EndpointJson.Options` + `SseWriter.WriteAsync`).

### `[Med]` `JsonElement` leaks across service boundaries

[`Services/ModelCatalogService.cs#L216-L233`](../Services/ModelCatalogService.cs): `LookupCatalogEntry` returns `JsonElement?`. The download service then re-extracts `uri`, `name`, `publisher` from raw JSON. Introduce a `CatalogEntry` record so downstream code binds to a stable shape.

### `[Med]` Inline SVGs and inline styles in Razor

[`Pages/Shared/_Layout.cshtml`](../Pages/Shared/_Layout.cshtml) and [`Pages/Index.cshtml`](../Pages/Index.cshtml) embed many large `<svg>` blobs and `style="..."` attributes. Move SVGs to a sprite or partials; move styles to classes.

### `[Low]` `Services/Platform/` grouping

Three unrelated statics live there (`BrowserLauncher`, `FoundryExecutable`, `UserPaths`). Either flatten into `Services/` or rename to `Platform/` at project root for clarity.

### `[Low]` Project / namespace / assembly mismatch

`RootNamespace = FoundryWebUI`, assembly name `FoundryWebUI-X`, project folder `FoundryWebUI-X`. Tooling that reads project name produces a dash; namespaces don't. Pre-existing — recommend leaving alone unless renaming wholesale.

---

## 5 — CI / release pipeline

### `[Med]` `dotnet format` only checks the app project

[`.github/workflows/ci.yml#L46-L48`](../.github/workflows/ci.yml). Test projects can drift.

### `[Med]` No coverage gate

`tests/Directory.Build.props` calls out `Microsoft.Testing.Extensions.CodeCoverage`, but CI never runs it. Either wire `dotnet run -- coverage` and upload to artifacts (and optionally to Codecov), or remove the comment.

### `[Low]` No supply-chain / security scanning

CodeQL, Dependabot, or Renovate would be cheap to add — useful given the `System.CommandLine` 2.0-rc dependency and Playwright's transitive surface.

### `[Low]` CI step naming

"Install Playwright browsers (chromium)" is accurate but the E2E project's name says "E2ETests"; either rename the workflow step or the project to make "Chromium only" explicit.

### `[Low]` `playwright install chromium --with-deps`

`--with-deps` is a no-op on macOS/Windows and may require sudo on some Linux runners; harmless on GitHub-hosted runners but worth a one-line comment.

---

## 6 — Small bugs & smells

### `[Med]` `ProbePortAsync` ignores caller cancellation

[`Services/EndpointDiscoveryService.cs#L105-L117`](../Services/EndpointDiscoveryService.cs) creates its own 5-second `CancellationTokenSource` and never links the caller's token.

### `[Med]` `SystemPrompt.Id` is only 8 hex chars

[`Services/SystemPromptStore.cs#L9`](../Services/SystemPromptStore.cs): `Guid.NewGuid().ToString("N")[..8]`. 32 bits; collisions are realistic over a long-lived prompt library. Use ≥ 12 hex chars.

### `[Med]` `ContextWindowLookup` silently warns on test paths

[`Services/ContextWindowLookup.cs#L15-L20`](../Services/ContextWindowLookup.cs) reads `WebRootPath` at construction. [`TestWebHostEnvironment.WebRootPath`](../tests/FoundryWebUI-X.TestInfrastructure/TestWebHostEnvironment.cs#L8) defaults to `Path.GetTempPath()`, so tests get an empty lookup with a warning — silently masking accidental dependence on context windows.

### `[Low]` `InMemoryLogSink.Snapshot` re-renders every event each call

[`Services/InMemoryLogSink.cs#L34-L64`](../Services/InMemoryLogSink.cs). On a Logs page polling at 1 Hz with a full buffer (2000 events), that's 2000 `RenderMessage` calls per poll. Cache the projection per event or limit poll cadence on the client.

### `[Low]` Inline styles in Razor

Multiple `style="..."` attributes throughout `Pages/`. Cosmetic but accumulates.

### `[Low]` `_count` increment-then-trim race in `InMemoryLogSink`

[`Services/InMemoryLogSink.cs#L20-L32`](../Services/InMemoryLogSink.cs): under heavy concurrency the count can briefly exceed `Capacity + 1`. Not user-visible; document as best-effort if not fixed.

---

## 7 — Implementation plan

A 7-phase plan ordered by **value × risk**, designed so each phase is independently mergeable and leaves CI green. Total estimated effort: ~3–4 focused days.

> ⚠️ Throughout: after each phase, run `dotnet format`, `dotnet build`, all three test suites, and verify CI is green before opening the next PR.

---

### Phase 1 — Documentation (½ day)

**Goal:** stop README from lying about the code. Smallest, highest-trust-recovering change.

**Steps:**

1. Edit [`README.md`](../README.md):
   - Fix macOS log path in the "Per-user data locations" table (`~/Library/Application Support/FoundryWebUI-X/logs/`).
   - Reword the "REST-only" bullet under Features:
     > REST-first — uses Foundry Local REST APIs directly. The `foundry` CLI is invoked only as a fallback for endpoint discovery and to start the service from the UI.
   - Either remove "Imposter" from the Test stack list or open a follow-up task to actually adopt it (recommend remove).
   - Document `--config <file>` failure mode: "If the file does not exist, startup fails."
2. Edit [`FoundryWebUI-X.csproj`](../FoundryWebUI-X.csproj) `<NoWarn>` comment: add the missing `CA1305` line.
3. Edit [`docs/deepseek_code_review.md`](deepseek_code_review.md): rename to `docs/2026-05-28_deepseek_code_review.md` and add a one-line "Superseded by `amp_code_review.md`" note at the top, OR delete.
4. Edit [`Pages/Shared/_Layout.cshtml#L74`](../Pages/Shared/_Layout.cshtml): replace `v1.0` with an injected version.
   - Add a tiny `ApplicationVersion` static or singleton service that reads `Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion`.
   - Inject into `_Layout.cshtml` via `@inject ApplicationVersion Version` and render `@Version.Display`.

**Verification:**

- `rg -n "Library/Logs/FoundryWebUI-X" README.md` → 0 results.
- `rg -n "REST-only" README.md` → 0 results.
- Visit `/` locally, confirm sidebar footer shows the MinVer version (e.g. `0.1.0-alpha.0`).

**Risk:** none.

---

### Phase 2 — Fork residue & dead code cleanup (½ day)

**Goal:** remove visible "this used to support Ollama" noise and pass-through facades.

**Steps:**

1. [`Models/LlmModels.cs`](../Models/LlmModels.cs):
   - Remove the `// "foundry" or "ollama"` comment on `ModelInfo.Provider`.
   - Remove `DownloadRequest.Provider` field. Update any caller (search: `rg -n "DownloadRequest" .`) — should only be `wwwroot/js/models.js`, which already only sends `modelId`.
   - Remove `ChatRequest.Stream` field. Verify `wwwroot/js/chat.js` does not depend on it (search: `rg -n '"stream"' wwwroot/js`).
2. Inline [`Services/InMemoryLogReader.cs`](../Services/InMemoryLogReader.cs):
   - Move `LogEntry` record onto `InMemoryLogSink`.
   - Update [`Services/InMemoryLogSink.cs#L55`](../Services/InMemoryLogSink.cs) to construct its own `LogEntry`.
   - Update DI registration in [`Program.cs#L179`](../Program.cs) and consumer in [`LogsEndpoints.cs`](../Endpoints/LogsEndpoints.cs) to inject `InMemoryLogSink` directly.
   - Delete `Services/InMemoryLogReader.cs`.
3. Decision on [`Services/FoundryLocalService.cs`](../Services/FoundryLocalService.cs):
   - **Recommended:** keep but `sealed`, since the README documents it as the provider abstraction.
   - Update [`Endpoints/StatusEndpoints.cs#L33`](../Endpoints/StatusEndpoints.cs), `ModelsEndpoints`, `ChatEndpoints`, etc. to consistently use the facade and **not** reach past it. Today some endpoints do both.
4. Remove unused `_logger` from [`Pages/Index.cshtml.cs`](../Pages/Index.cshtml.cs).
5. Decide on dead log sources in [`Endpoints/LogsEndpoints.cs`](../Endpoints/LogsEndpoints.cs):
   - **Recommended:** remove `app` and `stdout` cases since the UI doesn't expose them and the UI tests assert they're absent. Keep only `foundry`.
   - This also lets the route be `/api/logs/foundry` directly, and the `{source}` template can become a constant — simpler API.

**Verification:**

- `dotnet build` clean.
- `dotnet run --project tests/FoundryWebUI-X.UnitTests -c Release` green.
- `dotnet run --project tests/FoundryWebUI-X.IntegrationTests -c Release` green (note: [`EndpointTests.Logs_AppEndpoint_ReturnsEntries`](../tests/FoundryWebUI-X.IntegrationTests/EndpointTests.cs#L98-L112) will need to be deleted or rewritten to point at `foundry`; that test is no longer relevant if `app` is removed).
- Manual: launch app, confirm Logs page still works.

**Risk:** low. The only behavior change visible to users is the `/api/logs/app` endpoint disappearing (which the UI never called).

---

### Phase 3 — Test the riskiest code (1 day)

**Goal:** cover the two biggest currently-untested risks before any refactoring.

#### 3a. Extract & unit-test `ModelDeletionService` matching logic

1. Create `Services/ModelDirectoryMatcher.cs`:
   ```csharp
   internal static class ModelDirectoryMatcher
   {
       /// <summary>
       /// Locate the on-disk directory for a model id within a Foundry cache dir.
       /// Returns null when no match is found.
       /// </summary>
       public static string? FindModelDir(string cacheDir, string modelId, IFileSystem fs);
   }
   ```
2. Introduce a minimal `IFileSystem` seam (just `GetDirectories(string)` and `Exists(string)`) so tests can pass a fake tree without touching disk. Implement `RealFileSystem` for production.
3. Rewrite [`Services/ModelDeletionService.cs`](../Services/ModelDeletionService.cs) to call `ModelDirectoryMatcher.FindModelDir`, then perform the delete + log itself. Cut method length roughly in half.
4. Add `tests/FoundryWebUI-X.UnitTests/ModelDirectoryMatcherTests.cs` with at least these cases:
   - Exact match: `Microsoft/phi-3.5-mini-cpu-int4`.
   - Colon-to-dash transform: id `phi-3.5-mini:cpu-int4`.
   - Partial fallback: id matches by version-stripped prefix.
   - No match: returns `null`.
   - Multiple publishers: returns first match.

#### 3b. Test `EndpointDiscoveryService` cascade

This is harder because of static dependencies (`UserPaths`, `Process`). Two options:

- **Option A (recommended, larger refactor):** inject `IUserPaths` + `IFoundryCli` into the service.
- **Option B (smaller):** test only the pieces reachable today via config + the test handler:
  - Config endpoint set → returned verbatim.
  - Cached `_cachedEndpoint` returned on second call.
  - Fallback URL returned when nothing is set and probes fail.

Recommend **Option B** for this phase, with a TODO comment in the test file pointing at Option A.

#### 3c. Fix the no-op cancellation test

Edit [`tests/FoundryWebUI-X.UnitTests/ModelDownloadServiceTests.cs#L213-L233`](../tests/FoundryWebUI-X.UnitTests/ModelDownloadServiceTests.cs):
```csharp
[Test]
public async Task DownloadModelAsync_HandlesCancellation()
{
    var (svc, handler) = Build();
    handler.When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, CatalogJson);
    handler.When(HttpMethod.Post, "/openai/download", HttpStatusCode.OK, "Total  30.0% Downloading");

    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var results = new List<DownloadProgress>();
    await Assert.That(async () =>
    {
        await foreach (var p in svc.DownloadModelAsync("phi-3.5-mini", cts.Token))
        {
            results.Add(p);
        }
    }).Throws<OperationCanceledException>();

    await Assert.That(results.Any(r => r.Status == "complete")).IsFalse();
}
```

**Verification:**

- New tests in `ModelDirectoryMatcherTests` pass.
- Coverage (manual `dotnet run -- coverage` from any test project) shows `ModelDirectoryMatcher` near 100%, `ModelDeletionService` rising from 0%.
- `DownloadModelAsync_HandlesCancellation` now asserts and fails meaningfully if cancellation is broken.

**Risk:** low. Production code change is a refactor with a covering test; cancellation test fix can only catch more bugs, not introduce them.

---

### Phase 4 — Simplify `ModelDownloadService` + `ChatStreamingService` (½ day)

**Goal:** remove the `Channel` + `Task.Run` pattern in download; extract SSE parsing in chat.

#### 4a. `ModelDownloadService`

Rewrite [`DownloadModelAsync`](../Services/ModelDownloadService.cs#L26-L181) to:

1. Issue the POST.
2. On HTTP error → `yield return error` and `yield break`.
3. Loop on `ReadAsync(buffer)`:
   - Append to `lineBuffer`.
   - If a new `Total xx%` match exists since last yield → `yield return downloading`.
   - If a `success` JSON terminator → `yield return complete/error` and `yield break`.
   - Trim consumed prefix of `lineBuffer`.
4. After stream ends with no terminator → `yield return error` if `lastPercent < 99`, else `complete`.

This eliminates the channel, the Task.Run, and the silent OperationCanceledException swallow. Re-run the existing 9 tests to confirm parity.

#### 4b. `ChatStreamingService`

Extract `Services/Sse/SseEventParser.cs`:
```csharp
internal static class SseEventParser
{
    public static async IAsyncEnumerable<string> ParseAsync(
        Stream body,
        [EnumeratorCancellation] CancellationToken ct);
}
```
Yields the raw JSON payloads after `data: ` (or returns `[DONE]` as a sentinel).

Extract `Services/Sse/ChatErrorMapper.cs`:
```csharp
internal static class ChatErrorMapper
{
    public static (string? Code, string Message) Map(JsonElement errProp);
}
```

Rewrite [`ChatStreamingService.StreamChatAsync`](../Services/ChatStreamingService.cs#L22-L201) to use both. Target length: under 100 lines.

Add tests:
- `SseEventParserTests`: multi-line, partial lines, `[DONE]`, malformed JSON skip, `data:` and bare-JSON branches.
- `ChatErrorMapperTests`: string error, object error with code/type/message, missing fields.

**Verification:**

- All existing `FoundryLocalServiceTests` chat tests still pass.
- New parser/mapper tests pass.
- Manual: launch app, send a chat message, confirm streaming works.

**Risk:** medium. Touching the SSE parser is the closest this plan comes to user-visible regression. Mitigate with the new tests plus a manual smoke before merge.

---

### Phase 5 — Smaller cleanups & quick wins (½ day)

Batch the leftover `[Med]` and `[Low]` items:

1. **HttpClient leak** — Remove `EndpointDiscoveryService.HttpClient`. Each consumer service registers its own typed `HttpClient` in `Program.ConfigureServices`. Update constructors to accept `HttpClient` instead of `EndpointDiscoveryService.HttpClient` access. Status probes get a 5-second timeout; the download client keeps the 2-hour timeout but only `ModelDownloadService` uses it.
2. **Magic-number fallback endpoint** — Replace `"http://localhost:5272"` in [`EndpointDiscoveryService.cs#L102`](../Services/EndpointDiscoveryService.cs) with a documented constant `DefaultProbeEndpoint`, or throw a typed exception so callers surface the failure.
3. **Probe respects cancellation** — [`ProbePortAsync`](../Services/EndpointDiscoveryService.cs#L105-L117) takes a `CancellationToken` and links it.
4. **`SystemPrompt.Id` width** — Change `Guid.NewGuid().ToString("N")[..8]` to `[..12]` in [`SystemPromptStore.cs#L9`](../Services/SystemPromptStore.cs). Update or add a test asserting length.
5. **Shared SSE helper + JSON options** — Create `Endpoints/EndpointJson.cs` with a static `Options` and `SseWriter.WriteAsync(HttpContext, string, string)`. Remove duplication from [`ChatEndpoints.cs`](../Endpoints/ChatEndpoints.cs) and [`ModelsEndpoints.cs`](../Endpoints/ModelsEndpoints.cs).
6. **Drop jQuery** — Remove the `<script src="~/lib/jquery/dist/jquery.min.js">` line from [`_Layout.cshtml`](../Pages/Shared/_Layout.cshtml#L82). Smoke-test the four pages.
7. **`FoundryLocalService` sealed** — `public sealed class FoundryLocalService`.
8. **Empty page-models** — delete `Pages/{Logs,Models,Settings}.cshtml.cs`; verify pages still render.

**Verification:**

- All test suites green.
- Manual: launch app, click each page, confirm no JS errors in browser console.

**Risk:** low to medium (the jQuery removal is the loudest).

---

### Phase 6 — Move I/O out of endpoint handlers (½ day)

**Goal:** make `SetCacheDirectory` and `StartFoundry` DI-testable.

1. Create `Services/FoundryConfigService.cs`:
   - `Task<string?> GetCacheDirectoryAsync()` (already in `FoundryLocalService` via `EndpointDiscoveryService`; centralize).
   - `Task<UpdateCacheResult> UpdateCacheDirectoryAsync(string newPath)` returning a typed result (`Success | NotFound | InvalidPath | IoError`).
2. Move the bulk of [`SettingsEndpoints.SetCacheDirectory`](../Endpoints/SettingsEndpoints.cs#L35-L123) into the new service. Endpoint becomes a 20-line `IResult` translator.
3. Create `Services/FoundryProcessLauncher.cs`:
   - `Task<StartResult> StartAsync(CancellationToken)` returning exit code + stdout/stderr.
4. Move [`StatusEndpoints.StartFoundry`](../Endpoints/StatusEndpoints.cs#L44-L114) bulk into it. Endpoint becomes another small translator.
5. Add unit tests for both services. Use a `IProcessRunner` seam for `FoundryProcessLauncher` to keep tests fast.
6. Add integration tests for `/api/settings/cache-directory` (currently absent — see §3).

**Verification:**

- New unit tests cover the lifted logic.
- Integration tests for both endpoints pass.

**Risk:** low.

---

### Phase 7 — Frontend hygiene + CI (½ day)

#### 7a. CI improvements

1. [`.github/workflows/ci.yml#L46-L48`](../.github/workflows/ci.yml): expand the format step to all test projects:
   ```yaml
   - name: Format check
     run: |
       dotnet format FoundryWebUI-X.csproj --verify-no-changes --no-restore
       dotnet format tests/FoundryWebUI-X.UnitTests/FoundryWebUI-X.UnitTests.csproj --verify-no-changes --no-restore
       dotnet format tests/FoundryWebUI-X.IntegrationTests/FoundryWebUI-X.IntegrationTests.csproj --verify-no-changes --no-restore
       dotnet format tests/FoundryWebUI-X.E2ETests/FoundryWebUI-X.E2ETests.csproj --verify-no-changes --no-restore
   ```
2. Enable analyzers in tests: drop `<EnableNETAnalyzers>false</EnableNETAnalyzers>` from [`tests/Directory.Build.props`](../tests/Directory.Build.props). Resolve resulting warnings (likely a few CA1707/CA2007 in `NoWarn` already).
3. Optional but recommended: add a coverage step:
   ```yaml
   - name: Run unit tests with coverage
     run: dotnet run --project tests/FoundryWebUI-X.UnitTests/FoundryWebUI-X.UnitTests.csproj -c Release --no-build -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
   - uses: actions/upload-artifact@v4
     with:
       name: coverage-${{ matrix.os }}
       path: '**/coverage.cobertura.xml'
   ```
4. Add `.github/dependabot.yml` for `nuget` and `github-actions` ecosystems.

#### 7b. Frontend / Playwright

1. Add at least one real interaction test to `SmokeTests`:
   - Open `/`, click the model dropdown, pick the stub model, type "hello", click Send, wait for the SSE-mocked response to render, assert message bubble appears.
   - To do this, expand [`StubFoundryServer`](../tests/FoundryWebUI-X.E2ETests/Helpers/StubFoundryServer.cs) to also stub `/openai/load/...` and `/v1/chat/completions` with a short canned SSE stream.
2. (Stretch) split [`wwwroot/js/chat.js`](../wwwroot/js/chat.js) into ES modules. This is a larger lift; track as a follow-up.

**Verification:**

- CI run from a draft PR shows all three OSes green.
- Coverage artifact uploaded.
- New chat interaction E2E test passes locally and in CI.

**Risk:** medium. CI changes can be noisy; expect to iterate.

---

## Appendix A — Files at a glance

| Path | LoC | Issues | Tests |
|---|---:|---:|---:|
| `Program.cs` | 182 | 1 | indirect |
| `Endpoints/ChatEndpoints.cs` | 58 | 1 | indirect |
| `Endpoints/EndpointRegistry.cs` | 15 | 0 | — |
| `Endpoints/LogsEndpoints.cs` | 94 | 2 | partial |
| `Endpoints/ModelsEndpoints.cs` | 156 | 1 | partial |
| `Endpoints/SettingsEndpoints.cs` | 129 | 2 | none |
| `Endpoints/StatusEndpoints.cs` | 116 | 2 | partial |
| `Endpoints/SystemPromptsEndpoints.cs` | 65 | 0 | full |
| `Services/ChatStreamingService.cs` | 202 | 2 | partial |
| `Services/ContextWindowLookup.cs` | 48 | 1 | none |
| `Services/EndpointDiscoveryService.cs` | 370 | 4 | none |
| `Services/FoundryLocalService.cs` | 58 | 1 | full (delegation) |
| `Services/InMemoryLogReader.cs` | 20 | 1 (delete) | indirect |
| `Services/InMemoryLogSink.cs` | 65 | 1 | full |
| `Services/ModelCatalogService.cs` | 234 | 2 | partial |
| `Services/ModelDeletionService.cs` | 149 | 2 | **none** |
| `Services/ModelDownloadService.cs` | 182 | 2 | extensive |
| `Services/SystemPromptStore.cs` | 195 | 1 | full |
| `Services/Platform/BrowserLauncher.cs` | 123 | 0 | none |
| `Services/Platform/FoundryExecutable.cs` | 110 | 0 | partial |
| `Services/Platform/UserPaths.cs` | 50 | 0 | full |
| `Models/LlmModels.cs` | 65 | 3 | indirect |
| `wwwroot/js/chat.js` | 792 | 1 (large) | **none** |

## Appendix B — Out-of-scope ideas (worth a follow-up issue)

- Conversation persistence (already on the README roadmap).
- Frontend bundler (Vite / esbuild) — enables the chat.js split.
- Replacement for the on-disk model-cache deletion with a first-class Foundry API once one ships upstream.
- OpenTelemetry traces — Serilog is wired but no distributed tracing yet.
- Health check endpoint (`/healthz`) suitable for systemd / launchd watchers.
