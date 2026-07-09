# Clicky for Windows

This is a native Windows port of the publicly available, MIT-licensed Clicky
snapshot in [`../upstream-clicky`](../upstream-clicky). It is a tray-first
desktop companion that follows the original public interaction:

1. Open the Clicky panel from the tray.
2. Complete the local permission/setup flow.
3. Hold **Ctrl + Alt** to talk; release to submit.
4. Clicky records microphone audio while held, captures each display once only
   after release, sends the transcript and screenshots to your Worker, speaks
   the answer, and can point to a returned `[POINT:…]` target.

## Run it

Use the self-contained Windows build:

```text
release\Clicky.Windows.exe
```

The `release` directory contains the executable and its bundled .NET runtime;
copy the whole directory together. It does not require a machine-wide .NET
runtime installation.

## Enable real AI responses

The app intentionally does not accept or embed provider keys. Set an
owner-operated proxy URL before launching:

```powershell
$env:CLICKY_WORKER_URL = "https://your-worker.example/"
Start-Process .\release\Clicky.Windows.exe
```

That Worker must implement the public upstream routes:

- `POST /transcribe-token` for an AssemblyAI streaming token
- `POST /chat` as an Anthropic Messages streaming proxy
- `POST /tts` as an ElevenLabs MP3 proxy

The MIT upstream Worker is available at
[`../upstream-clicky/worker`](../upstream-clicky/worker). Deploy a copy you
control with your own provider credentials. The desktop app never sends raw
provider keys to a service and does not reuse the upstream's analytics or
email endpoints.

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
states that newer HeyClicky work is private, so this port faithfully targets
the public companion: tray UI, setup panel, Ctrl+Alt voice interaction,
multi-display capture, Worker-backed Claude/AssemblyAI/ElevenLabs integration,
and cursor pointing. It does not claim the private background-agent features
advertised on the current heyclicky site.

See [`ATTRIBUTION.md`](ATTRIBUTION.md) and [`UPSTREAM_LICENSE.md`](UPSTREAM_LICENSE.md)
for the upstream attribution and license.
