# Design QA — Clicky for Windows

## Comparison target

- Source visual truth: [`../upstream-clicky/clicky-demo.gif`](../upstream-clicky/clicky-demo.gif), extracted pointer crop at `C:\tmp\clicky-source-pointer-crop.png`, plus the public source UI specification in [`../upstream-clicky/leanring-buddy/CompanionPanelView.swift`](../upstream-clicky/leanring-buddy/CompanionPanelView.swift) and [`../upstream-clicky/leanring-buddy/DesignSystem.swift`](../upstream-clicky/leanring-buddy/DesignSystem.swift).
- Implementation: `C:\Users\jethi\AppData\Local\Temp\clicky-panel-render.png` rendered by the native Windows app in test mode.
- Same-input pointer comparison: `C:\tmp\clicky-pointer-comparison.png` — upstream demo on the left, Windows port on the right.
- Native panel viewport: `320 × 423` setup state; `320 × 275` ready state.
- Source-state caveat: the public repository does not include a screenshot of the menu-bar panel itself. Panel fidelity was therefore verified against its public SwiftUI source, while the cursor overlay was verified against the public demo capture.

## Full-view comparison evidence

The implementation preserves the source panel's dark 320px floating-surface composition: status header, divider rhythm, short privacy copy, permission rows, compact blue grant controls, Ctrl+Alt footer, and tray-first behavior. The ready state preserves the original hierarchy: a single instructional line, two-model selector, feedback/replay actions, and compact footer.

The source demo and Windows overlay were compared together in `C:\tmp\clicky-pointer-comparison.png`. The final pointer uses the source token `#3380FF`, a 16px equilateral shape, an 8px glow, and the source's default `-35°` rest orientation.

## Focused comparison evidence

- Panel header and setup rows: implementation capture at `C:\Users\jethi\AppData\Local\Temp\clicky-panel-render.png`.
- Ready-state model selector and status: same capture path after `--visual-test-ready`.
- Cursor shape, scale, color, and glow: `C:\tmp\clicky-pointer-comparison.png`.

No separate image-asset comparison was needed: the public source's panel is code-native SwiftUI and the only visible companion mark is its code-native triangle, not a missing raster/brand asset.

## Required fidelity surfaces

### Fonts and typography

Pass. The Windows port uses Segoe UI Variable with the source's compact 10–14px hierarchy, semi-bold title/actions, muted explanatory text, and single-line compact footer. Copy wraps without clipping at the 320px source width.

### Spacing and layout rhythm

Pass. The panel was corrected to the source's 320px width and content-driven height. Header/divider/section/footer cadence, 16px horizontal gutters, 8–18px section gaps, compact permission row density, and rounded 16px outer surface are all preserved.

### Colors and visual tokens

Pass. The implementation maps the public source tokens directly: `#101211` canvas, `#202221` surface, `#373B39` divider/border, `#ECEEED` primary text, `#ADB5B2` secondary text, `#6B736F` tertiary text, `#2563EB` actions, `#34D399` success, and `#3380FF` companion cursor.

### Image quality and asset fidelity

Pass. No target raster/logo asset was replaced with a placeholder. The source companion cursor is code-native and was implemented as a native WPF vector path, matching the upstream code's size/color/rotation rather than substituting stock imagery or an emoji.

### Copy and content

Pass with two intentional Windows adaptations:

- `Control+Option` becomes `Control+Alt`, the direct Windows equivalent.
- A small local-only notice is added until `CLICKY_WORKER_URL` is configured, so the port never implies that it has access to HeyClicky's private infrastructure or credentials.

## Comparison history

### Pass 1 — blocked

- [P2] Cursor mark was too large and vertically oriented.
  - Evidence: first side-by-side pointer comparison showed a 23×29px outlined arrow versus the source's compact 16px glowing triangle.
  - Fix: changed [`Views/CursorOverlayWindow.xaml`](Views/CursorOverlayWindow.xaml) to a 16×16 equilateral `#3380FF` triangle, removed the non-source outline, and applied the upstream's `-35°` resting rotation.

### Pass 2 — passed

- Post-fix evidence: `C:\tmp\clicky-pointer-comparison.png`.
- No actionable P0, P1, or P2 visual mismatches remain in the captured setup, ready, or idle-cursor surfaces.

## Interaction checks

- Tray-first launch, panel construction, setup state, ready state, model selection wiring, click-through overlay creation, Ctrl+Alt monitor, post-release-only capture sequence, cancellation path, pointer-tag parsing/mapping, Worker SSE request construction, and ElevenLabs playback integration compile into the self-contained Windows release.
- `dotnet build --no-restore -c Release` passed.
- `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\release` passed.
- Live provider calls and microphone-to-provider transcription were not executed because no owner-operated `CLICKY_WORKER_URL` and provider credentials were supplied. The app presents that as a clear local-only configuration state; it does not fall back to a fake cloud response.
- Native window capture through the Computer Use runtime was unavailable in this environment, so the actual WPF visual tree was rendered through a test-only native capture path. The release app defaults to capture exclusion for its own panel and overlay.

## Follow-up polish

- [P3] Windows does not expose macOS's identical TCC permission surfaces. The four-row source flow is preserved, but Screen Content is represented as a Windows display-capture acknowledgement.
- [P3] Add a signed installer when distribution requirements and a signing identity are known.

## Multi-provider and agent extension — 2026-07-10

- Added a dedicated provider settings window with Windows Credential Manager storage, configurable base URL/protocol/model, and live model discovery.
- Added OpenAI Responses, OpenAI-compatible Chat Completions, OpenRouter, MiMo, local, and custom endpoint paths.
- Added typed screen-aware questions plus a serial background task queue with visible queued/running/completed states.
- Added provider-specific web-search hooks for OpenAI Responses, OpenRouter plugins, and MiMo tools.
- Re-rendered the expanded ready panel at 320×424. The new provider card, prompt field, paired actions, task status, and legacy controls remain readable without horizontal clipping.
- Rendered the 460×620 provider settings window. The first pass exposed unreadable light-on-light native ComboBox text; the final pass uses dark selected text and is readable across provider, protocol, and model controls.
- `dotnet build --no-restore -c Release` passed with zero warnings and zero errors after the extension.
- The updated self-contained `win-x64` release publish passed and its executable remained running through the native visual-test launch.
- Live paid-provider calls remain configuration-dependent and were not attempted without user-owned API credentials.

## Final result

passed
