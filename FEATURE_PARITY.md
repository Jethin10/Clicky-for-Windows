# Public HeyClicky parity audit

Audit date: 2026-07-11

Authoritative public snapshot:

- `farzaa/clicky` commit `a80fa80721a8aebe51a170a7780705024ebc6e46`
  (2026-04-27)
- <https://www.heyclicky.com/>
- <https://www.heyclicky.com/privacy>

“Verified” means the current Windows branch has reachable implementation code
and proportionate runtime/test evidence. Files that exist upstream but have no
call site are not counted as working features. Claims such as “do whatever” are
not treated as finite requirements without a concrete public example.

| Public behavior | Windows evidence | Status |
| --- | --- | --- |
| Tray/menu-bar companion without a normal main window | `TrayService`, taskbar-hidden WPF panels, native launch probe | Verified |
| Cursor-adjacent companion and global hold-to-talk shortcut | `OverlayHost`, `CursorOverlayWindow`, `ModifierPushToTalkMonitor` | Verified |
| Capture only after push-to-talk and never continuously while idle | `CompanionHost.CompleteInteractionAsync`, capture-exclusion flags, privacy tests/QA | Verified |
| See all connected screens and prioritize the cursor screen | `ScreenCaptureService.CaptureAllScreens`, labeled dimensions, multi-monitor point mapping | Verified |
| Streaming AssemblyAI transcription with OpenAI and platform speech fallbacks | Worker token/WSS/event contract probes, direct transcription loopback, and real offline PCM tests | Verified |
| Spoken responses with ElevenLabs-style, compatible, and platform voice paths | Worker/direct TTS loopback contract probes plus Windows speech synthesis fallback | Verified |
| Screen-aware conversational teaching with the last ten exchanges | both model clients plus bounded `_conversationHistory`; Responses/chat/Worker multimodal-history contract probes | Verified |
| Animated blue cursor pointing on any monitor | point-tag parser, screenshot-to-screen mapping, Bézier flight overlay, native render | Verified |
| Permission/onboarding panel, model choice, replay, and quit controls | `CompanionPanelWindow`, permission rows, provider/model settings, replay/quit actions | Verified |
| “HeyClicky agent” launches work without blocking the foreground interaction | voice router and serial background-agent queue | Verified |
| Turn the visible Figma design into working webpage files | screen captures plus bounded file package, native approval, atomic workspace writes/backups | Verified |
| Research products like the visible camera under a budget | contract-tested OpenAI, OpenRouter, and MiMo web-search hooks plus persistent full agent-result viewer | Verified |
| Summarize a PDF and email it to the supplied team | selectable/scanned PDF extraction, local OCR, bounded email proposal, explicit SMTP approval | Verified |
| Full research result remains retrievable after the background task finishes | latest-result panel and `AgentResultWindow`, complete-result copy, 500,000-character bound | Verified |
| Replay the upstream cursor-following onboarding video | Same public Mux asset in a tracking-disabled WebView2 iframe, cursor-following native host, timed screen demo, local fallback | Verified |
| Menu/tray panel dismisses when the user clicks elsewhere | deferred WPF deactivation dismissal with owned-window preservation; native focus-transfer probes | Verified |
| Optional anonymous product analytics | Deliberately absent; no first-party endpoint or consent contract is published for this independent app | Not implemented by privacy choice |

## Non-features excluded from the requirement set

- `CompanionResponseOverlay.swift` is not instantiated by the public Mac app;
  the active model callback explicitly discards its chunks.
- `ElementLocationDetector.swift` also has no reachable call site; active
  pointing uses model-produced `[POINT:...]` tags instead.
- Proprietary behavior not described on the official website cannot be audited
  from the older MIT source and is not claimed as implemented.
