# VoiceFlow — Windows System-Wide Dictation App

> Hold a key, speak, release → transcript lands at the cursor in any app.
> Native Windows tray app. No cloud API. Fully local via whisper.cpp.

---

## What this is

A port of a macOS menu-bar dictation app to Windows. Runs as a system tray app (no window, no taskbar entry). Hold **Right Alt**, speak, release → audio is transcribed locally by [whisper.cpp](https://github.com/ggml-org/whisper.cpp) and pasted at the cursor in whatever app is focused — Gmail, Slack, VS Code, terminal, anything.

Built in C# / .NET 8 WinForms. No subscription, no API key, no internet required after the one-time model download.

---

## Project structure

```
VoiceFlow/
├── Program.cs              # Entry point + MessagePumpForm (hidden WinForms pump)
├── TrayApp.cs              # Orchestrator: hook → recorder → transcriber → paste
├── KeyboardHook.cs         # WH_KEYBOARD_LL on a dedicated raw Win32 message loop thread
├── AudioRecorder.cs        # NAudio 16kHz mono WAV capture, 60s cap
├── WhisperTranscriber.cs   # Shells out to whisper-cli.exe, reads transcript
├── ClipboardPaster.cs      # Clipboard save → Ctrl+V → restore at 600ms
├── PillForm.cs             # Borderless topmost floating status pill
├── StartAtLogin.cs         # Windows Startup folder shortcut (.lnk via WScript.Shell)
├── NativeMethods.cs        # P/Invoke: SetWindowsHookEx, SendInput, GetModuleHandle
├── VoiceFlow.csproj        # .NET 8 WinForms, NAudio 2.2.1, post-build whisper copy
├── setup.ps1               # One-time: downloads whisper.cpp binary + ggml-small model
└── whisper/                # Created by setup.ps1
    ├── whisper-cli.exe
    ├── whisper.dll
    ├── ggml.dll
    ├── ggml-base.dll
    ├── ggml-cpu.dll
    └── ggml-small.bin      # ~150MB, downloaded once
```

---

## Quick start

### Prerequisites
- Windows 10/11, x64
- [.NET 8 SDK](https://aka.ms/dotnet/download)
- PowerShell (built into Windows)

### 1 — Clone and run setup

```powershell
git clone https://github.com/olajhabi/Amazon-projects.git
cd Amazon-projects\voiceflow-windows\VoiceFlow
.\setup.ps1
```

`setup.ps1` downloads:
- `whisper-cli.exe` and required DLLs from the [whisper.cpp releases](https://github.com/ggml-org/whisper.cpp/releases/tag/b5130)
- `ggml-small.bin` model (~150MB) from HuggingFace

### 2 — Build and run

```powershell
dotnet build -c Release
.\bin\Release\net8.0-windows\VoiceFlow.exe
```

Or publish a self-contained single exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained -o publish
.\publish\VoiceFlow.exe
```

### 3 — Use it

- A gray circle appears in the system tray (expand hidden icons with the `^` chevron if needed)
- Click into any text field — Notepad, browser, Slack, VS Code, anything
- **Hold Right Alt** for at least 0.3 seconds, speak, release
- Pill window near the bottom of screen shows Recording → Transcribing → done
- Transcript pastes at the cursor

### 4 — Start at login (optional)

Right-click the tray icon → **Start at Login**. Creates a `.lnk` shortcut in your Windows Startup folder.

---

## Architecture

### Core loop

```
Right Alt held
    └─ KeyboardHook (WH_KEYBOARD_LL on dedicated raw Win32 thread)
         └─ RecordingStarted event
              └─ AudioRecorder.StartRecording()  [NAudio, 16kHz mono WAV]
Right Alt released (≥300ms)
    └─ KeyboardHook → RecordingStopped event
         └─ AudioRecorder.StopRecording() → last.wav
              └─ WhisperTranscriber.TranscribeAsync()
                   └─ shells out to whisper-cli.exe -m ggml-small.bin -f last.wav -otxt
                        └─ reads .txt output
                             └─ ClipboardPaster.PasteTextAsync()
                                  └─ clipboard save → SetText(transcript) → SendInput(Ctrl+V) → restore at 600ms
```

### Tray states

| State | Icon color | Pill |
|---|---|---|
| Idle | Gray | Hidden |
| Recording | Red (pulsing) | "Recording…" |
| Transcribing | Blue | "Transcribing…" |
| Error | Orange | "Error — check tray" |

---

## Windows-specific design decisions and known constraints

This section documents every platform-specific issue encountered during the port. Read this before changing anything architectural.

### 1. Trigger key — Right Alt, not Fn

**Why not Fn:** On HP EliteBooks and most Windows laptops, `Fn` is trapped by the embedded controller firmware before Windows sees it. It does not appear as a VK code in `WH_KEYBOARD_LL` under normal conditions (some HP BIOS settings can expose it, but don't design around that without testing).

**Why Right Alt works:** `VK_RMENU` (0xA5) is a real, hookable VK code. Right Alt fires `WM_SYSKEYDOWN` (0x0104) and `WM_SYSKEYUP` (0x0105), not the regular `WM_KEYDOWN`/`WM_KEYUP` — the hook must handle both message types, which it does.

**To change the trigger key:** update `VK_TRIGGER` in `KeyboardHook.cs`:
```csharp
private const uint VK_TRIGGER = 0xA5; // Right Alt. Change to e.g. 0x91 for Scroll Lock
```

Common alternatives: `0x91` (Scroll Lock), `0x14` (Caps Lock), `0xA3` (Right Ctrl if present).

### 2. WH_KEYBOARD_LL message pump — must use a raw Win32 loop

**The problem:** `WH_KEYBOARD_LL` requires an active message pump on the thread that installed the hook. If that thread goes idle (no messages to process), Windows silently stops delivering hook callbacks — no error, no warning, the hook just stops working.

WinForms `Application.Run()` with no visible form goes idle immediately and stops pumping. `Application.Run(form)` with a hidden form also goes idle. `Application.Idle` callbacks don't keep the loop alive.

**The fix used here:** `KeyboardHook` spins up a dedicated background thread and runs a raw Win32 `GetMessage` / `TranslateMessage` / `DispatchMessage` loop. This loop blocks on `GetMessage` and never goes "idle" in the WinForms sense — it always returns when there are messages (including hook callbacks) and blocks otherwise. The hook is installed on this thread, and callbacks fire reliably.

```csharp
// KeyboardHook.cs — pump thread core
while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
{
    TranslateMessage(ref msg);
    DispatchMessage(ref msg);
}
```

**Do not** replace this with `Application.Run()`, `Thread.Sleep()`, or a spin loop. The raw `GetMessage` loop is the correct approach and matches what native C++ keyboard hook applications do.

### 3. Main WinForms app uses a hidden MessagePumpForm

`TrayApp` is not an `ApplicationContext` — it's a plain class instantiated from `MessagePumpForm.OnLoad`. `MessagePumpForm` is a 1×1 invisible WinForms form passed to `Application.Run()`. This ensures:
- The WinForms message loop starts before `TrayApp` constructor runs
- Cross-thread `Invoke()` calls (for pasting, tray icon updates) have a valid handle to marshal through
- The app exits cleanly when `Application.Exit()` is called

### 4. No Right Ctrl key on this machine

The target HP EliteBook replaces the Right Ctrl key with a **Copilot key**. Right Alt is used instead. This is the default in this codebase.

### 5. whisper.cpp binary location

`setup.ps1` downloads `whisper-bin-Win32.zip` from release tag `b5130` of `https://github.com/ggml-org/whisper.cpp`. The binaries live in a `Release/` subfolder inside the zip. Setup copies `whisper-cli.exe` and its required DLLs (`whisper.dll`, `ggml.dll`, `ggml-base.dll`, `ggml-cpu.dll`) one level up to `whisper/`.

The `.csproj` has a post-build target that copies the entire `whisper/` folder into the output directory (`bin/Release/net8.0-windows/whisper/`) so the app always finds it at `AppContext.BaseDirectory/whisper/whisper-cli.exe`.

### 6. whisper.cpp release URL changed

Older releases were at `github.com/ggerganov/whisper.cpp`. As of build `b5130`, the repo moved to `github.com/ggml-org/whisper.cpp`. The setup script uses the correct URL.

### 7. Corporate/managed machine risk (open)

If this machine has Defender for Endpoint, CrowdStrike, or similar EDR: a `WH_KEYBOARD_LL` global keyboard hook matches the exact signature of keylogger behavior and may be quarantined. If the app disappears from the tray after a few minutes, EDR quarantine is the likely cause. Resolution: code-sign the exe and add it to the EDR allow-list.

### 8. Microphone privacy (check if dictation captures silence)

Windows Settings → Privacy & security → Microphone → must allow desktop apps. On a managed device this may be locked by group policy. If recordings consistently produce empty transcripts, check this setting first.

### 9. SendInput won't paste into elevated windows

`SendInput` synthesizing `Ctrl+V` is blocked by Windows UIPI if the target app runs at a higher integrity level than VoiceFlow (e.g. an admin-elevated terminal). Standard limitation — no workaround without running VoiceFlow as admin too.

---

## Files reference

### `KeyboardHook.cs`
- Installs `WH_KEYBOARD_LL` via `SetWindowsHookEx` on a dedicated STA thread
- Runs a raw `GetMessage` loop to keep callbacks alive
- Handles `WM_SYSKEYDOWN`/`WM_SYSKEYUP` (required for Alt keys) in addition to `WM_KEYDOWN`/`WM_KEYUP`
- 300ms tap debounce — taps shorter than this fire `RecordingCancelled` (no transcription)
- Writes `%LOCALAPPDATA%\VoiceFlow\keylog.txt` for debugging (remove the `Log()` calls for production)

### `AudioRecorder.cs`
- `WaveInEvent` (NAudio) at 16kHz, 16-bit, mono — the format whisper.cpp expects
- Auto-stops at 60 seconds, fires `RecordingTimedOut`
- Writes to `%LOCALAPPDATA%\VoiceFlow\last.wav` (overwritten each recording)

### `WhisperTranscriber.cs`
- Shells out: `whisper-cli.exe -m ggml-small.bin -f last.wav -otxt -of <tmpfile> -np -nt`
  - `-np` = no progress output, `-nt` = no timestamps in transcript
- Reads `<tmpfile>.txt`, trims whitespace, returns transcript
- 60-second process timeout
- Checks `whisper-cli.exe` and `ggml-small.bin` exist before launching — shows setup balloon if missing

### `ClipboardPaster.cs`
- Saves existing clipboard text
- Sets transcript via `Clipboard.SetText()`
- Sends `Ctrl+V` via `SendInput` (4 key events: Ctrl↓ V↓ V↑ Ctrl↑)
- Restores original clipboard 600ms later on a fresh STA thread

### `PillForm.cs`
- Borderless, topmost, `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT` (never steals focus, click-through)
- Rounded region (22px radius)
- Pulsing red dot while recording (40ms timer, alpha 80↔255)
- Positioned at screen bottom-center, 40px from taskbar
- Hidden in Idle state

### `setup.ps1`
- Downloads `whisper-bin-Win32.zip` from `github.com/ggml-org/whisper.cpp` release `b5130`
- Copies exe and DLLs to `whisper/` flat directory
- Downloads `ggml-small.bin` from HuggingFace (~150MB)
- Safe to re-run — skips files already present

---

## Build & publish

```powershell
# Debug run
dotnet run

# Release build
dotnet build -c Release

# Self-contained single-folder publish (no .NET runtime required on target machine)
dotnet publish -c Release -r win-x64 --self-contained -o publish
```

---

## Open questions / v1.1 candidates

- [ ] **Confirm Fn key** — test on the actual EliteBook BIOS settings; some HP models expose Fn via HP Hotkey Support utility
- [ ] **EDR/AV allow-list** — if the machine has managed endpoint protection, the exe will need to be code-signed and added to the allow-list
- [ ] **Error toasts** — balloon tip on transcription failure (network, mic, rate limit). Partially implemented (shows error state), full toast text TBD
- [ ] **Trigger key remap UI** — currently requires recompiling to change `VK_TRIGGER`; a settings menu item would be cleaner
- [ ] **local whisper.cpp fallback** — currently the only mode; a Groq/OpenAI cloud fallback could be added as a faster option
- [ ] **Remove debug logging** — `keylog.txt` and `app.log` in `%LOCALAPPDATA%\VoiceFlow\` log every keypress; strip before wider distribution
- [ ] **Part B: Study Typewriter VS Code extension** — separate build, not in this repo yet

---

## Related

- Original macOS app: `voiceflow/` (SwiftUI, not in this repo)
- whisper.cpp: https://github.com/ggml-org/whisper.cpp
- NAudio: https://github.com/naudio/NAudio
