# Model Capability Badges — Design Spec

## Overview

Add visual capability badges to the Models page table and Chat page dropdown so users can quickly identify what each model supports (vision, tool calling, code, reasoning, speech) without reading model names.

## Data Sources

Capabilities are derived from the Foundry Local catalog (`/foundry/list`):

| Capability | Source | Detection |
|---|---|---|
| Vision | `task` field | `=== "vision-language-chat"` |
| Speech | `task` field | `=== "automatic-speech-recognition"` |
| Tool Calling | `supportsToolCalling` field | `=== true` |
| Reasoning | Model name | Contains `reasoning` or `r1-distill` (case-insensitive) |
| Code | Model name | Contains `coder` (case-insensitive) |

These fields are already returned by the Foundry catalog but are not currently passed through to the frontend.

## Backend Changes

### ModelInfo class (`Models/LlmModels.cs`)

Add a `Capabilities` list property:

```csharp
public List<string>? Capabilities { get; set; }
```

Values: `"vision"`, `"tools"`, `"code"`, `"reasoning"`, `"speech"`. Nullable so loaded models (which are sparse before merge) can be distinguished from enriched models with no capabilities (empty list `[]`). After the merge in `GetModels`, all models will have a non-null list.

### FoundryLocalService (`Services/FoundryLocalService.cs`)

In `GetAvailableModelsAsync()`, populate `Capabilities` from the catalog entry:
- Read `task` and `supportsToolCalling` directly from the JSON.
- Infer `reasoning` and `code` from the `displayName` field.

In `GetLoadedModelsAsync()`, the loaded models are sparse (just IDs). The merge logic in `ApiController.GetModels` already enriches loaded models from catalog data — extend it to also copy `Capabilities`.

### ApiController (`Controllers/ApiController.cs`)

In the `GetModels` merge loop, add: `m.Capabilities ??= catModel.Capabilities;`

No other backend changes needed. The catalog also provides `fileSizeMb` which is already exposed as `Size`.

## Frontend — Models Page Table

### Style: Pill Badges (Style A)

A new **Capabilities** column is added to the table (between Status and Size). The existing Device column stays.

Each capability renders as a colored pill badge with an inline SVG icon and text label:

| Capability | Color | Icon |
|---|---|---|
| Vision | Blue (`#58a6ff`) | Eye/iris |
| Tools | Yellow (`#d29922`) | Lightbulb (tool calling) |
| Reasoning | Purple (`#a371f7`) | Question circle (thinking) |
| Code | Green (`#3fb950`) | Angle brackets `</>` |
| Speech | Red (`#f85149`) | Microphone |

Badge CSS reuses the existing `.badge-status` pattern: `display: inline-flex`, translucent colored background, rounded pill shape, uppercase text, 0.65rem font.

All badges include a `title` attribute with a description (e.g., "Vision / multimodal image input").

Models with no capabilities show a dash `—`.

### Table column changes

Add `<th>` for "Capabilities" in `Models.cshtml`. Not sortable in this iteration. The `renderModels()` function in `models.js` generates the badge HTML from the `capabilities` array.

## Frontend — Chat Page Dropdown

### Custom dropdown component (replaces native `<select>`)

A native `<select>` cannot render icons, colors, or custom HTML. The model selector becomes a custom dropdown:

- A styled trigger div showing the selected model name (or "Select a model" placeholder).
- A floating options list that opens on click.
- Each option row contains: model name, capability icon badges, file size, and a RAM-fit icon.
- Keyboard navigation: arrow keys, Enter to select, Escape to close, type-ahead search.

### Style: Icon-Only with Rich Tooltip (Style C)

Each dropdown option shows:

```
[model name]                    [cap icons] [size] [RAM icon]
```

- **Capability icons**: Small (18×18px) colored squares with the SVG icon, no text label. Same color scheme as the table.
- **Size**: Monospace text (e.g., `4.9 GB`).
- **RAM-fit icon**: A RAM/DIMM stick SVG colored green/yellow/red/gray based on estimated RAM vs. system RAM ratio (same thresholds as the Models page "Can Run" column: ≤50% green, ≤75% yellow, >75% red, unknown gray).

### Rich tooltip on hover

When the user hovers a dropdown row, a floating tooltip appears **overlaying the same row** (right-aligned, vertically centered). It contains **all** capabilities as full pill badges (icon + text label), using the same pill style as the Models page.

Tooltip styling:
- Background: `#1c2128`
- Border: `1.5px solid rgba(88, 166, 255, 0.25)`
- Box shadow: `0 4px 16px rgba(0,0,0,0.5), 0 0 0 1px rgba(88,166,255,0.1)`
- Border radius: 8px
- `pointer-events: none` so it doesn't interfere with clicking the option
- Appears on row hover, disappears on mouse leave

Models with no capabilities show no tooltip.

### RAM icon

Replace the plain colored dot with a RAM/DIMM stick SVG (16-bit retro memory module shape with pins). Colored by the same green/yellow/red/gray scheme. Includes a `title` tooltip ("Comfortable — uses less than 50% of RAM", etc.).

This icon is used in both the custom dropdown and can optionally replace the text badges in the Models page "Can Run" column in a future iteration.

## CSS Organization

New CSS classes go in `site.css` under a new `/* ── Capability Badges ── */` section:

- `.cap-badge` — pill badge base (icon + text)
- `.cap-badge.vision`, `.cap-badge.tools`, `.cap-badge.reasoning`, `.cap-badge.code`, `.cap-badge.speech` — color variants
- `.cap-icon` — icon-only square badge (dropdown)
- `.cap-icon.vision`, etc. — color variants
- `.cap-tooltip` — rich tooltip container
- `.custom-select` — custom dropdown component styles
- `.ram-icon` — RAM stick icon container

## Shared Icon/Capability Map

A single JavaScript object in `site.js` defines the capability metadata (SVG paths, colors, labels, CSS classes) so both `models.js` and `chat.js` reference the same source of truth. This avoids duplicating SVG strings across files.

```javascript
const CAPABILITY_MAP = {
    vision:    { label: 'Vision',    css: 'vision',    color: '#58a6ff', title: 'Vision / multimodal image input', svg: '...' },
    tools:     { label: 'Tools',     css: 'tools',     color: '#d29922', title: 'Tool / function calling support', svg: '...' },
    reasoning: { label: 'Reasoning', css: 'reasoning', color: '#a371f7', title: 'Chain-of-thought reasoning model', svg: '...' },
    code:      { label: 'Code',      css: 'code',      color: '#3fb950', title: 'Optimized for code generation', svg: '...' },
    speech:    { label: 'Speech',    css: 'speech',     color: '#f85149', title: 'Speech-to-text / automatic speech recognition', svg: '...' },
};
```

## File Changes Summary

| File | Change |
|---|---|
| `Models/LlmModels.cs` | Add `Capabilities` property to `ModelInfo` |
| `Services/FoundryLocalService.cs` | Populate `Capabilities` in `GetAvailableModelsAsync()` |
| `Controllers/ApiController.cs` | Copy `Capabilities` in merge loop |
| `wwwroot/css/site.css` | Add capability badge and custom dropdown CSS |
| `wwwroot/js/site.js` | Add shared `CAPABILITY_MAP` and RAM icon SVG |
| `wwwroot/js/models.js` | Render Capabilities column with pill badges |
| `wwwroot/js/chat.js` | Replace `<select>` with custom dropdown, render icon badges + rich tooltip |
| `Pages/Models.cshtml` | Add Capabilities `<th>` column |
| `Pages/Index.cshtml` | Update markup for custom dropdown (replace `<select>` with `<div>`) |

## Out of Scope

- Sorting/filtering by capability on the Models page (future enhancement).
- Replacing the Models page "Can Run" column with the RAM icon (keep existing badge for now).
- Capability detection for non-Foundry providers.
