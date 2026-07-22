# VoiceType Roadmap

A running list of ideas and planned features for VoiceType. Nothing here is committed to a
schedule — it's a backlog to capture direction. Items may change, be reordered, or dropped.

Status legend: 🟢 planned · 🟡 idea / needs design · ✅ done

---

## Planned / ideas

*Items are listed in intended implementation order.*

### 1. 🟡 Streaming (live) transcription polish
Refine the existing `Stream` mode UX and partial-result handling.

- **Why:** the backend already supports a `Stream` mode alongside `Server`/`Cli`, but its UX
  is less polished than push-to-talk.
- **Ideas:** show interim/partial text in the floating pill (or a small preview) as it arrives,
  smoother state transitions, and clear handling of the final vs. partial result.
- **Reuse:** the overlay states (`ShowPreparing`/`ShowListening`/`ShowProcessing`) and the
  status-pill sizing work already done are a foundation for a live-preview affordance.
- **Open question:** insert-as-you-go vs. insert-on-finalize; how to correct earlier partials.

### 2. 🟡 First-run setup / model download helper
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

### 3. 🟡 Selected-text voice rewrite
Allow users to select text, dictate an editing instruction, and replace the selection with a
locally processed rewrite.

- **Why:** it extends fast dictation into revision and formatting workflows without requiring a
  separate editor.
- **Flow:** capture the current selection through the existing clipboard-safe input infrastructure,
  record a voice instruction through a separate hotkey, preview the proposed result, then replace
  only after explicit confirmation.
- **Scope:** start with deterministic local transformations where feasible; any future LLM-backed
  provider must be opt-in, disclose data transfer, and remain separate from normal offline
  dictation.
- **Safety:** preserve clipboard content, retain the original selected text until confirmation, and
  never execute dictated commands or modify files.
- **Reference:** FluidVoice's edit/rewrite mode may help identify user workflow expectations, but
  VoiceType should prioritize local, confirmation-based transformations.
- **Open question:** define the initial local transformation set and preview UX before considering
  remote or local model providers.

### 4. 🟡 Hardware capability guidance
Detect the available transcription runtime and guide users toward a practical model and mode.

- **Why:** model size, RAM, CPU features, and installed whisper.cpp backends strongly affect the
  dictation experience; users should not need to infer compatible choices from logs.
- **Detection:** report CPU architecture, installed whisper executables/backends, available models,
  and optionally Windows-supported GPU adapters without making unsupported acceleration claims.
- **UI:** show a read-only diagnostics panel and recommendations such as a model size, `Cli` versus
  `Server` mode, and whether real-time streaming is available.
- **Safety:** recommendations are advisory, explain their basis, and never silently change a
  user's model, backend, or transcription settings.
- **Reference:** FluidVoice's hardware-selection UX can be consulted for product ideas; implement
  detection against VoiceType's whisper.cpp runtimes and validate every recommendation locally.
- **Open question:** which GPU/runtime checks are sufficiently reliable across packaged builds.

### 5. 🟡 Installer and startup at Windows login *(deferred)*
Package VoiceType with a proper installer and offer startup at login as an independently usable
setting.

- **Why:** it's a background tray utility; most users will want it always running, and a real
  installer makes distribution and updates far cleaner than a raw folder.
- **Startup setting:** a `StartWithWindows` (bool) on `VoiceTypeSettings`, surfaced as a checkbox
  in the Settings window and usable before a full installer exists.
- **Implementation options:** (a) `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  registry value pointing at the installed exe path, or (b) a Startup-folder shortcut. Registry
  is simpler for a single-user, no-admin install.
- **Robustness:** write the installed executable path, and keep the registry entry in sync when
  the checkbox is toggled (add on enable, remove on disable).
- **Deferred:** implement alongside the installer effort so exe paths and lifecycle are stable.

### 6. 🟡 Local concise-prompt transformation for AI coding assistants
Convert long natural-language dictation into a concise, technically precise prompt when the user is
dictating into an AI coding assistant such as GitHub Copilot.

- **Why:** users can explain a development task naturally, while the inserted prompt preserves the
  task, context, constraints, and expected output with less unnecessary wording.
- **Context:** use the focused application/control and an explicit transformation hotkey to select
  a Copilot-oriented profile; normal dictation remains unchanged unless the user enables an
  application profile.
- **Initial provider:** evaluate a locally hosted `flan-t5-small` text-generation model, loaded
  lazily on the first transformation request rather than at application startup.
- **Lightweight fallback:** add deterministic lexical normalization for filler removal, spoken
  punctuation, and custom replacements. It must not attempt semantic task/context/constraint
  extraction or promise a structured prompt when no language model is available.
- **Safety:** preserve technical identifiers, file names, versions, numbers, error codes, and
  explicit constraints; show a preview initially; fall back to the cleaned transcript on timeout,
  model failure, or validation failure. The transformer must rewrite the prompt, not solve the
  programming task or execute dictated commands.
- **Privacy:** keep the initial provider local and make any future cloud provider explicitly opt-in.
- **Scope:** `all-MiniLM-L6-v2` is not required initially because it cannot generate the concise
  prompt; semantic validation can be considered later if model quality requires it.
- **Open questions:** ONNX Runtime versus a separate local process, transformation hotkey versus
  automatic application profiles, model output-quality benchmarks, and the idle timeout for
  unloading the transformation model.

---

## Contributing ideas

Add new entries under the appropriate section with a status marker. When an item ships,
mark it ✅ (or remove it once released).
