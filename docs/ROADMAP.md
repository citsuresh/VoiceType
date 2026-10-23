# VoiceType Roadmap

A running list of ideas and planned features for VoiceType. Nothing here is committed to a
schedule — it's a backlog to capture direction. Items may change, be reordered, or dropped.

Status legend: 🟢 planned · 🟡 idea / needs design · ✅ done

---

## Planned / ideas

*Items are listed in intended implementation order.*

### 1. 🟡 Streaming (live) transcription polish
Show stable live text while the user speaks, without the visible rewriting the current `Stream`
mode produces.

- **Why:** the backend already supports a `Stream` mode alongside `Server`/`Cli`, but live text
  keeps changing as the user speaks, so it was never usable as a live display.
- **Root cause (Whisper limitation, not an app bug):** Whisper is not a streaming model.
  `whisper-stream` fakes streaming by re-transcribing a sliding window from scratch every step
  (current args `--step 1000 --length 4000 --keep 200`: every ~1 s it re-transcribes the last
  ~4 s). Each pass sees more audio, so earlier words get revised ("I want to" → "I wanted to").
  `whisper-stream` also redraws the whole current line, so `WhisperStreamClient` receives
  full-line replacements rather than appended words, and the trailing words are always the least
  stable.
- **Current non-streaming flow (for reference):** in `Server`/`Cli` mode, the full clip is recorded
  to `./temp/voicetype_session_<ts>.wav` (16 kHz mono 16-bit), transcribed once on release (HTTP
  POST to the local `whisper-server` `/inference` or a `whisper-cli` run), cleaned, inserted, and
  the WAV is deleted. No live text exists in these modes; `PreviewChunkMilliseconds` is exposed in
  Settings but is not read by `DictationSessionController`.
- **Option A — keep Whisper, add committed/tentative split (short-term):** apply a
  "LocalAgreement" policy — a word becomes *committed* only once two consecutive passes agree on
  it. Committed text is shown normally and never changes; the unstable tail is shown dimmed/grey.
  Trade-off: no new engine and change is local to `WhisperStreamClient` + the pill, but committed
  text lags speech by ~1–2 s and accuracy stays at the current model's level.
  Reference: [ufal/whisper_streaming](https://github.com/ufal/whisper_streaming) (concept only).
- **Option B — true streaming model:** see item 2 (sherpa-onnx streaming Zipformer, or Moonshine
  streaming). These models only append words and never re-transcribe earlier audio.
- **Option C — hybrid preview + final pass:** see item 2. A streaming model drives the live pill;
  on release a more accurate offline model transcribes the full recording and only that result is
  inserted.
- **Rule for every option:** insert text only on finalize (key release / toggle stop), never
  insert-as-you-go — so text already typed into the target app never needs to be corrected.
- **Reuse:** the overlay states (`ShowPreparing`/`ShowListening`/`ShowProcessing`) and the
  status-pill sizing work already done are a foundation for a live-preview affordance.
- **Open questions:** how much preview text the pill can show (last N words vs. expandable
  preview), and whether Option A is still worth doing if item 2 lands.

### 2. 🟡 Alternative local speech engine (sherpa-onnx: Parakeet + streaming Zipformer)
Add an in-process transcription backend that offers faster/more accurate final text and true
streaming live text, alongside the existing whisper.cpp backends.

- **Why:** Whisper `base.en` (current default, `Server` mode) has moderate accuracy, no real
  streaming, and depends on an external `whisper-server.exe` listening on a TCP port. On
  2026-10-23 that port (51234) fell inside a Windows dynamic port exclusion range
  (51229–51328, see `netsh interface ipv4 show excludedportrange protocol=tcp`), so the server
  failed to bind and the app silently fell back to `Cli`. An in-process engine has no port.
- **Privacy:** all candidates run fully on-device; the network is used only for the one-time
  model download. Verify during a prototype by transcribing with the network disabled / the app
  blocked in Windows Firewall.
- **Engine comparison** (approx. English WER from the Hugging Face Open ASR Leaderboard, from
  memory — re-verify before deciding; speed/memory are estimates, not measured on this machine):

  | Model | Approx. WER | Size | CPU speed | Live text | Languages | Notes |
  |---|---|---|---|---|---|---|
  | Whisper base.en (current) | ~10% | 141 MB | Fast | Only via `whisper-stream` re-transcribing | English | External exe/server |
  | Whisper small.en (installed) | ~8.5% | 465 MB | Moderate | Same | English | |
  | Whisper large-v3-turbo q5 (installed) | ~7.8% | 547 MB | Slow on CPU, fine on GPU | Same | ~99 | Zero-code accuracy upgrade if a GPU is available |
  | NVIDIA Parakeet TDT 0.6B (via sherpa-onnx) | ~6–6.3% | ~650 MB int8 | Very fast | No (offline, final pass) | v2 English; v3 25 European | Better punctuation/casing, fewer silence hallucinations |
  | Streaming Zipformer (via sherpa-onnx) | ~8–12% | 30–300 MB | Very fast, low CPU | Yes, append-only | Per model (mostly en/zh) | Weaker punctuation |
  | Moonshine Voice (streaming) | ~10% | 30–250 MB | Very fast; compute scales with audio length | Yes, built-in | Mainly English | MIT; C API only, needs a C# P/Invoke wrapper |
  | Kyutai STT 1B / 2.6B | ~6–7% (2.6B) | 2–5 GB | Needs a strong GPU | Yes (0.5 s / 2.5 s delay) | en+fr / en | Rust/Python server; not a fit for CPU dictation |

- **Recommended engine:** [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) — official
  C# API (NuGet `org.k2fsa.sherpa.onnx`), Windows x64/arm64, both streaming and non-streaming ASR,
  built-in Silero VAD, runs in-process (no exe, no HTTP, no port). One library covers both the
  preview and final models below. Other references:
  [moonshine-ai/moonshine](https://github.com/moonshine-ai/moonshine),
  [kyutai-labs/delayed-streams-modeling](https://github.com/kyutai-labs/delayed-streams-modeling)
  (concept/evaluation only).
- **Deployment setups to choose from:**

  | Setup | Models | Trade-off |
  |---|---|---|
  | C — hybrid (preferred) | Streaming Zipformer preview + Parakeet final | Best preview and best accuracy; highest memory (~100–300 MB + ~0.7–1 GB) |
  | Streaming only | One streaming model; its live text is also inserted | Lightest, instant insert on release; accuracy ≈ base.en |
  | Final only | Parakeet only, no live text | Same UX as today but faster and more accurate; no extra memory for a preview model |
  | C with Whisper final | Streaming preview + existing base.en/small.en | Reuses installed models; final accuracy stays at Whisper's level |
  | Single model (to investigate) | A sherpa-onnx streaming FastConformer used for both preview and final | Possibly one model with good enough accuracy; unverified |

  Make the live preview a setting: on = hybrid, off = final only (no preview model loaded).
- **Hybrid audio pipeline (Option C):** `AudioCaptureService` captures once; each chunk is fed to
  (1) the streaming recognizer for pill text and (2) an in-memory buffer of the whole utterance.
  On release the buffer goes to the offline recognizer and its result is inserted. The two models
  run sequentially (preview while speaking, final after release), so they don't compete for CPU.
  sherpa-onnx accepts in-memory samples, so the temp WAV becomes optional (keep only if needed
  for history/debugging). Both models stay loaded to avoid per-dictation load time.
- **Preview vs. final mismatch handling:** the final text will sometimes differ from the preview
  (punctuation, casing, a misheard word; rarely whole sentences). This is acceptable because the
  preview is a draft and nothing is inserted until the final result is ready. UX options:
  1. Style the preview as a draft (dimmed/italic); show "Finalizing…" on release.
  2. Briefly show the final text in the pill (~1 s) after insertion so changes are visible.
  3. Extend `TranscriptDiffService` / the bulb / `ComparisonWindow` with the preview as an
     additional stage so users can see which words the final pass corrected.
  4. Setting: "Accurate (final model)" vs. "Fast (insert live text as shown)".
  5. Fallback: if the final pass fails or exceeds a timeout, insert the preview text instead of
     nothing.

  Suggested defaults: 1 + 2 + 5, with 3 as an enhancement and 4 optional.
- **Integration:** add as a new backend (e.g. `Mode = Sherpa`) next to `Cli`/`Server`/`Stream`,
  keeping whisper.cpp as a fallback. Needs settings for engine/model selection and the preview
  toggle, and a model download path (ties into item 3, first-run model download helper).
- **Costs/risks:** new native dependency (ONNX Runtime via sherpa-onnx), model downloads of
  ~100–700 MB, audio must be fed as 16 kHz mono float samples (capture already uses 16 kHz mono),
  and more resident memory in hybrid mode.
- **Next step when picked up:** a small prototype comparing base.en vs. Parakeet (latency + output
  on the user's own recordings) and streaming Zipformer preview stability, run offline to confirm
  no network use.
- **Interim workaround for the port issue (independent of this item):** change
  `WhisperServerPort` to a port outside the excluded ranges (e.g. 49152 or 5100), or have the app
  probe for a free port at startup.

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

### 4. 🟡 Selected-text voice rewrite
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

### 5. 🟡 Hardware capability guidance
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

### 6. 🟡 Installer and startup at Windows login *(deferred)*
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

### 7. 🟡 Local concise-prompt transformation for AI coding assistants
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
