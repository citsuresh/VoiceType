# VoiceType Roadmap

A running list of ideas and planned features for VoiceType. Nothing here is committed to a
schedule — it's a backlog to capture direction. Items may change, be reordered, or dropped.

Status legend: 🟢 planned · 🟡 idea / needs design · ✅ done

---

## Planned / ideas

*Items are listed in intended implementation order.*

### 1. 🟡 Custom word replacements / dictionary
Per-user substitutions for names, jargon, and acronyms whisper tends to mis-hear.

- **Why:** proper nouns and domain terms (product names, people, acronyms) are commonly wrong.
- **Data:** a user-editable list of `from → to` pairs (case-insensitive match, whole-word by
  default), stored in settings or a sidecar file (e.g. `replacements.json`).
- **UI:** a small editable grid in the Settings window (add/remove rows).
- **Integration:** apply at the dedicated extension point in the `CleanTranscript` pipeline,
  after filler-word removal.
- **Open question:** plain string replace vs. regex support; word-boundary handling and
  preserving surrounding casing.

### 2. 🟡 Streaming (live) transcription polish
Refine the existing `Stream` mode UX and partial-result handling.

- **Why:** the backend already supports a `Stream` mode alongside `Server`/`Cli`, but its UX
  is less polished than push-to-talk.
- **Ideas:** show interim/partial text in the floating pill (or a small preview) as it arrives,
  smoother state transitions, and clear handling of the final vs. partial result.
- **Reuse:** the overlay states (`ShowPreparing`/`ShowListening`/`ShowProcessing`) and the
  status-pill sizing work already done are a foundation for a live-preview affordance.
- **Open question:** insert-as-you-go vs. insert-on-finalize; how to correct earlier partials.

### 3. 🟢 Language selection UI
Expose a language picker in the Settings window so users aren't limited to the default.

- **Why:** whisper.cpp supports many languages, but the current UI only exposes a free-text
  `Language` box (defaulting to `en`) — a curated picker would be friendlier.
- **Model layer:** the `Language` property on `VoiceTypeSettings` already exists and persists to
  JSON (ISO code like `en`, or `auto` for auto-detect).
- **UI:** replace the free-text `LanguageTextBox` in `SettingsWindow.xaml` with a `ComboBox`.
  Populate with a curated list of common languages plus an **Auto-detect** option. Bind selection
  into the shared settings singleton in `SettingsWindow.xaml.cs` → save path, persisted via
  `SettingsLoader.SaveAsync`.
- **Backend wiring:** already wired — the language flows as `-l <lang>` in `WhisperProcessRunner`,
  `WhisperServerClient`, and `WhisperStreamClient`. Note `.en` models are English-only, so `auto`
  only makes sense on multilingual models.
- **Apply timing:** live on the next dictation session (or a server restart in `Server` mode).
- **Open question:** short curated list vs. full whisper language list; whether "Auto" is
  reliable enough to be the default.

### 4. 🟡 First-run setup / model download helper
Guide new users to download a Whisper model on first launch.

- **Why:** model binaries are intentionally **not** committed to git (too large for GitHub's
  100 MB limit; see `.gitignore` and README). A fresh clone has no model, so the app can't
  transcribe until one is placed in `whisper.cpp/models/`.
- **Current behavior:** there is no first-run flow — startup silently logs `model: (not found)`
  and the tray **Model** submenu shows a disabled `(no models found)` placeholder.
- **Detection:** on startup, reuse `WhisperProcessRunner.EnumerateModels()` /
  `ResolveModelsDirectory()` — if no `ggml-*.bin` is found, enter a first-run flow instead of
  failing silently.
- **Flow:** a simple window (or guided tray flow) that lists recommended models
  (e.g. `base.en`, `small.en`) with sizes, downloads the chosen one into the models folder with
  a progress indicator, then makes it the active model.
- **Reuse:** once downloaded, it slots straight into the existing tray **Model** submenu and
  model-switch path.
- **Open question:** download source/URLs and checksum verification; whether to bundle a tiny
  model vs. always download.

### 5. 🟡 Tray-click toggle (hands-free) dictation mode
Let users start/stop dictation by clicking the tray icon instead of holding a hotkey.

- **Why:** the current hotkey is **hold-to-talk** (`HotkeyPressed → StartSessionAsync`,
  `HotkeyReleased → StopSessionAsync` in `App.xaml.cs`). A hands-free toggle is nicer for longer
  dictation where holding a key is awkward.
- **Tray interaction:**
  - **Single left-click** → toggle listening (idle → `StartSessionAsync`, active → `StopSessionAsync`).
  - **Double-click** → open Settings (keep the existing behavior).
  - **Right-click menu** → add a **checkable menu item** ("Toggle mode") to enable/disable the
    feature, in sync with the Settings checkbox.
- **Single vs. double click:** since single-click fires before double-click, distinguish them with
  a short timer (`SystemInformation.DoubleClickTime`): on click, start the timer; if a double-click
  arrives first, cancel the toggle and open Settings instead. Use `NotifyIcon.MouseClick`/
  `MouseDoubleClick` filtered to `MouseButtons.Left` so right-click just opens the context menu.
- **Icon state:** add a second (recording) icon asset and a `SetListeningState(bool)` on
  `TrayIconManager` that swaps `_notifyIcon.Icon` (white ↔ green/recording) and updates
  `_notifyIcon.Text` ("VoiceType — Listening…"). Dispose the previously owned icon (`_ownedIcon`).
- **State ownership:** drive the icon from the controller's session state (single source of truth)
  so it stays correct if a session ends on its own; marshal updates to the UI thread.
- **Coexistence with hold-to-talk:** the controller rejects a start if a session is already
  active, so tray toggle and the hold hotkey can't fight; optionally reflect the recording icon
  for hold-mode sessions too.
- **Settings — dedicated "Toggle mode" section:**
  - **Enable checkbox:** `UseTrayIconToggle` (bool) on `VoiceTypeSettings` (e.g. "Use tray icon
    for toggle mode"), persisted via `SettingsLoader.SaveAsync` and kept in sync with the tray
    context-menu checkbox.
  - **Idle auto-stop:** "Automatically stop listening if the mic is idle for X seconds" — a bool
    (`ToggleIdleAutoStopEnabled`) plus an editable integer (`ToggleIdleAutoStopSeconds`).
- **Idle detection (feasible today):** reuse the existing amplitude signal — `AudioCaptureService`
  (`WaveInEvent`) already produces a normalised amplitude (0..1) fed to
  `BreathingOverlayWindow.CurrentAmplitude`. Track the last time amplitude exceeded a small
  threshold; if it stays below for the configured seconds, call `StopSessionAsync`. No new audio
  pipeline needed; consider a short warm-up grace period so it doesn't stop before the user speaks.
- **Safety:** optional max-duration auto-stop so a forgotten toggle session doesn't record forever.
- **Open question:** silence threshold tuning/default; whether to also swap the tray icon during
  hold-mode sessions for consistency.

### 6. 🟡 Installer and Auto-start on Windows login *(deferred)*
Package VoiceType with a proper installer and, as part of it, offer auto-start at login.

- **Why:** it's a background tray utility; most users will want it always running, and a real
  installer makes distribution and updates far cleaner than a raw folder.
- **Auto-start setting:** a `StartWithWindows` (bool) on `VoiceTypeSettings`, surfaced as a
  checkbox in the Settings window.
- **Implementation options:** (a) `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  registry value pointing at the installed exe path, or (b) a Startup-folder shortcut. Registry
  is simpler for a single-user, no-admin install.
- **Robustness:** write the installed executable path, and keep the registry entry in sync when
  the checkbox is toggled (add on enable, remove on disable).
- **Deferred:** implement alongside the installer effort so exe paths and lifecycle are stable.

---

## Contributing ideas

Add new entries under the appropriate section with a status marker. When an item ships,
mark it ✅ (or remove it once released).
