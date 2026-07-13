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

## Direct voice and spoken-agent extension — 2026-07-10

- Added configurable OpenAI-compatible `/audio/transcriptions` and `/audio/speech` support with separate optional audio credentials in Windows Credential Manager.
- Added complete bounded PCM recording, 16 kHz mono WAV encoding, direct TTS playback, and Worker TTS fallback.
- Added spoken `HeyClicky agent` routing while preserving ordinary Ctrl+Alt screen questions.
- Added a dependency-free smoke-test project. It validates compact/spaced wake phrases, ordinary-question routing, WAV headers, audio configuration, pointer tags, and a real loopback HTTP transcription request including path, bearer header, multipart WAV payload, and response parsing.
- `dotnet build --no-restore -c Release` passed with zero warnings and zero errors.
- `dotnet run --project .\Tests\Clicky.Windows.SmokeTests.csproj -c Release` passed.
- Rendered the audio settings section at 460×620. All controls, model values, credential status, and action buttons are readable without horizontal clipping.

## Local document and PDF extension — 2026-07-10

- Added a compact attachment card and local file picker for PDFs plus common text and code formats.
- Added 25 MB file and 120,000-character context bounds, persistent follow-up attachment state, local-only extraction before submission, and explicit scanned-PDF failure messaging.
- Added PdfPig 0.1.15 extraction using content-order text reconstruction.
- Generated a deterministic two-page PDF with ReportLab, verified two pages with `pdfinfo`, rendered both pages through Poppler at 144 DPI, and visually inspected them with no clipping, overlap, or missing content.
- The smoke suite extracted both pages in order and validated the expected phrases.
- Rendered the 320×478 ready panel with an attached PDF. File name, page/character count, remove action, prompt, and agent controls remain readable without horizontal clipping.

## Approval-gated agent file builds — 2026-07-10

- Added a bounded agent artifact protocol for up to 20 text files and 2,000,000 total characters.
- Added lexical workspace containment, absolute/traversal/duplicate-path rejection, symbolic-link and junction rejection, re-checks before writes, atomic UTF-8 replacement, rollback on failure, and persistent overwrite backups.
- Added a 620×560 native approval window showing workspace, summary, create/overwrite action, character count, and selected-file preview. Users must explicitly check the review confirmation before file writes are enabled.
- The first visual pass exposed unreadable selected-row text from the native ListView theme; the final pass uses a dark selected foreground and all columns are readable.
- Smoke tests passed for artifact parsing, create plans, actual atomic file writes, overwrite detection, overwrite backups, and traversal rejection.

## Final result

## Tray-panel click-outside dismissal — 2026-07-12

- Added deferred deactivation handling so the tray panel closes after focus
  moves to another app, matching the public menu-bar panel behavior.
- Preserved the panel whenever a visible owned Clicky window is active, covering
  provider settings, email/file approvals, and full agent results.
- Ran two native focus-transfer probes: an unowned transparent window caused
  dismissal, while an owned transparent window kept the panel visible.
- The full regression suite and self-contained native launch remain green.

## Cursor-following onboarding video — 2026-07-12

- Added replayable playback of the exact public upstream Mux asset in a
  non-activating 370×252 window that follows the active cursor across screens.
- Embedded Mux's official iframe player with tracking and cookies disabled;
  WebView data is stored under Clicky's writable local app-data directory.
- Added a bounded manifest probe, 72-second lifecycle, 40-second optional
  screen-aware pointing demonstration, cancellation on push-to-talk, and a
  12-second local tutorial fallback.
- Captured and visually inspected a real video frame and the forced offline
  fallback card. Both fill the player area and remain readable without clipping.
- Verified the upstream HLS duration as 67.776 seconds and added tests for HTTPS,
  official player host, privacy query flags, and iframe composition.

## Persistent background-agent results — 2026-07-11

- Replaced the lossy 180-character completion bubble with a compact latest-result
  card and a resizable native viewer containing the complete result.
- Added task, provider, completion-time, scrolling, and explicit copy controls.
- Kept the cursor notification short while preserving up to 500,000 result
  characters until the next background task completes.
- Rendered the 680×620 result viewer and the expanded companion panel; long
  research text, metadata, preview, View action, and footer controls remain
  readable without clipping.
- Added tests for full-result preservation, document-context removal from task
  labels, one-line previews, and the result safety bound.

## Windows offline speech fallback — 2026-07-11

- Added Windows Desktop Speech recognition as a local-only fallback for failed,
  empty, or unconfigured cloud transcription.
- Added Windows text-to-speech as the final voice-output fallback after direct
  and Worker speech services.
- Recognition consumes only the existing bounded push-to-talk PCM recording
  after key release; no ambient recognition service was introduced.
- Rendered the focused settings state with three installed offline recognizers,
  sixteen local voices, automatic language selection, and voice controls
  readable without clipping.
- Synthesized a deterministic 16 kHz mono phrase, passed its raw PCM through the
  production recognition method, and verified cloud-success and cloud-failure
  routing independently.

## Local scanned-document OCR — 2026-07-11

- Added local OCR for image attachments and pages without selectable PDF text.
- Preserved page order for mixed searchable/scanned PDFs and labeled OCR-derived
  pages in model context.
- Bundled English OCR data so recognition never needs a document upload or a
  runtime model download.
- Verified a generated pixels-only PDF had no selectable text, visually
  inspected its rendered page, and recovered its expected phrases in the smoke
  suite.
- Added 25 MB, 120,000-character, and 50 scanned-page safety limits.

## Approval-gated email delivery — 2026-07-11

- Added bounded email proposals with validated To/Cc recipients, subject, and
  plain-text body.
- Added a native review window that shows the complete message and keeps Send
  disabled until the explicit confirmation checkbox is selected.
- SMTP passwords remain in Windows Credential Manager and are never displayed
  in the approval window or stored in the JSON settings file.
- Added a deterministic email-approval snapshot mode and a loopback SMTP test
  that verifies a real MailKit delivery without contacting an external server.

## Provider protocol contract validation — 2026-07-12

- Exercised OpenAI Responses, generic OpenAI-compatible chat completions,
  OpenRouter, MiMo, and the owner-operated Claude Worker against deterministic
  local HTTP/SSE servers.
- Verified exact route selection, bearer and OpenRouter attribution headers,
  screenshot/history serialization, provider-specific web-search fields, and
  cumulative streaming text callbacks.
- Separated TTS synthesis transport from speaker playback and verified direct
  OpenAI-compatible and Worker/ElevenLabs-style endpoint bodies and audio bytes
  without requiring paid credentials, internet access, or an audio device.
- Release build completed with zero warnings and the expanded smoke suite passed.

## AssemblyAI streaming contract validation — 2026-07-12

- Verified the owner-operated Worker's `POST /transcribe-token` route and JSON
  token extraction against a local HTTP server.
- Verified the production secure WebSocket host/path, 16 kHz signed PCM
  encoding, formatted-turn mode, `u3-rt-pro` model, and escaped token query.
- Exercised Begin, partial Turn, formatted/final Turn, and Error event parsing
  without requiring an AssemblyAI credential or recording ambient audio.

## Remote Windows validation — 2026-07-12

- Added a bounded Windows GitHub Actions job for every pull request and push to
  `main` or an `agent/**` branch.
- The job cleanly restores and builds both the app and smoke-test executable,
  runs the tests from the newly built assembly, audits transitive NuGet
  vulnerabilities, and publishes a self-contained x64 artifact for 14 days.
- Rehearsing the exact job locally caught and corrected both stale test-binary
  reuse and an invalid no-restore RID publish before the workflow was pushed.
- Pull-request run `29203853021` then passed all steps on GitHub's clean
  `windows-latest` runner, including artifact upload.

## Live OpenAI failure-path validation — 2026-07-13

- Authenticated model discovery with a user-supplied project key and confirmed
  that the configured `gpt-5.6-luna` model is visible.
- A live Responses stream returned an HTTP-200 SSE `error` event for
  `insufficient_quota`; direct speech returned the equivalent HTTP 429.
- Fixed the universal streaming client to surface direct and `response.failed`
  provider errors instead of silently returning an empty response, with a
  deterministic regression probe for the observed event shape.

## Windows onboarding reachability fix — 2026-07-13

- Removed the macOS-shaped hard gate that required four pseudo-permissions and
  an email address before typed mode became reachable.
- Accessibility, global hotkey, and display capture are presented as ready on
  Windows; microphone settings remain optional for voice input.
- Added an always-reachable **Continue to Clicky** action and persisted its
  completion in local settings so restarts open directly to the ready panel.
