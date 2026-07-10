# Clicky for Windows

This is a native Windows port of the publicly available, MIT-licensed
[`farzaa/clicky`](https://github.com/farzaa/clicky) snapshot. It is a tray-first
desktop companion that follows the original public interaction:

1. Open the Clicky panel from the tray.
2. Complete the local permission/setup flow.
3. Hold **Ctrl + Alt** to talk; release to submit.
4. Clicky records microphone audio while held, captures each display once only
   after release, sends the transcript and screenshots to your chosen model, speaks
   the answer, and can point to a returned `[POINT:…]` target.

## Run it

Use the self-contained Windows build:

```text
release\Clicky.Windows.exe
```

The `release` directory contains the executable and its bundled .NET runtime;
copy the whole directory together. It does not require a machine-wide .NET
runtime installation.

## Connect an AI provider

Open **AI provider settings** from the tray menu or the Clicky panel. The app
supports:

- OpenAI through the Responses API
- OpenRouter through OpenAI-compatible Chat Completions
- Xiaomi MiMo through its OpenAI-compatible API
- local servers such as LM Studio, Ollama-compatible gateways, or vLLM
- any custom OpenAI-compatible base URL

Clicky can discover model IDs from the provider's `GET /models` route, while
still allowing manual model IDs for endpoints that do not expose discovery.
Disable **Send current screen captures** when using a text-only model.
API keys are stored in Windows Credential Manager and are never written to the
settings file or repository. The saved non-secret configuration lives at
`%LOCALAPPDATA%\Clicky\settings.json`.

Provider documentation:

- [OpenAI API quickstart](https://platform.openai.com/docs/quickstart)
- [OpenRouter API reference](https://openrouter.ai/docs/api/reference/overview)
- [Xiaomi MiMo first API call](https://mimo.mi.com/docs/quick-start/first-api-call)

## Voice services

Direct model access powers typed screen questions and background agent tasks.
The original public Clicky voice path still expects an owner-operated Worker
for streaming transcription and speech. Set it before launching:

```powershell
$env:CLICKY_WORKER_URL = "https://your-worker.example/"
Start-Process .\release\Clicky.Windows.exe
```

That Worker must implement the public upstream routes:

- `POST /transcribe-token` for an AssemblyAI streaming token
- `POST /chat` as an Anthropic Messages streaming proxy
- `POST /tts` as an ElevenLabs MP3 proxy

The MIT upstream Worker is available in
[`farzaa/clicky/worker`](https://github.com/farzaa/clicky/tree/main/worker). Deploy a copy you
control with your own voice-provider credentials. Clicky does not reuse the
upstream's analytics or email endpoints. Direct model keys saved in Windows
Credential Manager are sent only to the base URL shown in provider settings.

## Background agents

Type a request in the Clicky panel and choose **Run agent**. Tasks execute in a
serial background queue, can use provider web-search support when enabled, and
return a visible completion result beside the cursor. The agent prompt forbids
claiming that an external action happened without a real tool result.

This version intentionally does not yet give agents unrestricted filesystem,
browser, shell, or email access. Those action tools require explicit approval,
auditing, and recovery controls before they are safe to ship.

## Privacy behavior

- No desktop capture runs while idle.
- Capture happens only after Ctrl+Alt is released and only for the current
  interaction.
- Clicky's windows are marked to exclude themselves from normal Windows screen
  capture. The app also hides its overlay for its own capture pass as a
  fallback.
- Windows secure desktop, UAC prompts, DRM-protected content, and elevated
  applications can remain unavailable to ordinary desktop capture.

## Scope

The upstream repository is an older open-source Clicky snapshot. Its README
states that newer HeyClicky work is private. This project does not access or
copy that private source. Newer behavior is independently implemented from
public product descriptions: typed screen-aware help, a provider-independent
model layer, and an approval-conscious background task queue.

See [`ATTRIBUTION.md`](ATTRIBUTION.md) and [`UPSTREAM_LICENSE.md`](UPSTREAM_LICENSE.md)
for the upstream attribution and license.
