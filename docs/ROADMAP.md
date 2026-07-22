# VoiceType Roadmap

A running list of ideas and planned features for VoiceType. Nothing here is committed to a
schedule — it's a backlog to capture direction. Items may change, be reordered, or dropped.

Status legend: 🟢 planned · 🟡 idea / needs design · ✅ done

---

## Planned / ideas

*Items are listed in intended implementation order.*

> **External reference:** [FluidVoice for Windows](https://github.com/huslermaniac/fluidvoice-windows)
> can be consulted for product ideas where noted below. It is GPLv3; use it only for conceptual
> reference and independently design and implement all VoiceType code.

### 1. 🟡 Custom word replacements / dictionary
Per-user substitutions for names, jargon, and acronyms whisper tends to mis-hear.

- **Why:** proper nouns and domain terms (product names, people, acronyms) are commonly wrong.
- **Data:** a user-editable list of `from → to` pairs (case-insensitive match, whole-word by
  default), stored in settings or a sidecar file (e.g. `replacements.json`).
- **UI:** a small editable grid in the Settings window (add/remove rows).
- **Integration:** apply at the dedicated extension point in the `CleanTranscript` pipeline,
  after filler-word removal.
- **Reference:** FluidVoice has a user-configured custom dictionary; use it only as product
  inspiration for defining a focused VoiceType experience.
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

### 3. 🟡 First-run setup / model download helper
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

### 4. 🟡 Transcription history
Keep an opt-in, local-only record of completed transcriptions.

- **Why:** users often need to recover, copy, or reuse text after it has been inserted.
- **Data:** persist timestamp, text, selected model/language, and insertion outcome in a bounded
  local store. Do not record audio by default.
- **UI:** add an on-demand History window with copy, delete, clear, and retention-limit controls.
- **Privacy:** history is disabled by default or clearly consented to on first enable; provide a
  one-click clear action and a documented on-disk location.
- **Reference:** FluidVoice includes transcription-history workflows; use it only to inform
  feature scope and privacy expectations.
- **Open question:** JSON versus SQLite, and whether failed/empty transcriptions belong in history.

### 5. 🟡 Selected-text voice rewrite
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

### 6. 🟡 Hardware capability guidance
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

### 7. 🟡 Installer and startup at Windows login *(deferred)*
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

### 8. 🟡 Local concise-prompt transformation for AI coding assistants
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

### 9. 🟡 Additional post-processing settings
Expand transcript post-processing into independently configurable settings pages under the
`Post-processing` navigation category.

- **Normalization:** retain the existing whitespace, capitalization, and trailing-punctuation
  options.
- **Filler word removal:** retain the existing user-editable filler list and add the conservative
  non-lexical defaults already identified; keep removal independently enableable.
- **Spoken punctuation:** add an independently enabled, user-editable phrase-to-punctuation rule
  list for terms such as `comma`, `period`, `question mark`, `new line`, and `new paragraph`.
- **Custom word replacements:** add an independently enabled user-editable dictionary for technical
  terms, names, acronyms, and application-specific replacements.
- **Custom removal rules:** add an initially empty, independently enabled user-editable collection
  named `CustomRemovalRules`. Each rule supports a phrase and scope: start of sentence, end of
  sentence, or anywhere in a sentence.
- **Safety:** keep ordinary dictation behavior unchanged unless the relevant setting is enabled;
  do not add built-in conversational removal phrases or attempt semantic task/context/constraint
  extraction.
- **Open question:** whether spoken-punctuation and custom-rule settings apply to all dictation or
  only to a future explicit concise-prompt mode; concise-prompt-only is the safer default.

---

## Contributing ideas

Add new entries under the appropriate section with a status marker. When an item ships,
mark it ✅ (or remove it once released).
