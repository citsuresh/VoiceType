# DESIGN_DECISIONS

> Append-only, dated log of non-obvious architectural/design choices. Never delete or rewrite
> prior entries. If a decision is reversed, add a new entry referencing the old one.

## 2025 (retroactive entries, inferred from existing code)

- **Windowless, tray-first UI.** VoiceType has no main window; the tray icon, a floating
  click-through status pill, and an on-demand Settings window are the entire UI surface.
  Rationale: dictation is meant to be unobtrusive and not steal focus from the app being dictated
  into.

- **Three interchangeable transcription backends behind one controller.** `WhisperProcessRunner`
  (CLI, per-utterance process), `WhisperServerClient` (long-lived server, model stays resident),
  and `WhisperStreamClient` (real-time streaming) all plug into
  `DictationSessionController`/`TranscribeWavAsync`. Rationale: lets users trade off idle RAM vs.
  latency (see README "Choosing settings for your RAM") without changing the session/insertion
  code.

- **Server mode falls back to CLI on failure, but not on timeout.** In `TranscribeWavAsync`, a
  failed server request retries via CLI; a *timed-out* request does not, since falling back would
  reload and re-run the same (large) model and likely time out again. Rationale: avoid doubling
  wait time on an already-slow path; surface the timeout directly instead.

- **`ISettingsSection` contract for a searchable master-detail Settings window.** Each settings
  page is a self-contained `UserControl` implementing `Title`, `SearchKeywords`, `Load`,
  `Validate`, `Save`. `SettingsWindow` owns navigation, search filtering, and a single global
  Save/Cancel across all sections. Rationale: keeps each settings page independently testable and
  addable without touching a monolithic settings form; search keywords let users find a field by
  name even if it's not in the section title.

- **Transcript post-processing as a fixed-order, always-partially-on pipeline.** A first stage
  (ANSI escapes, transcript gutters, duplicate lines, a literal non-speech marker list) always
  runs and is not configurable. Subsequent normalization steps (trim, collapse spaces, capitalize
  sentences, remove filler words, add trailing period) are individually toggleable via settings,
  in a fixed code order. Rationale: keep behavior deterministic and cheap (runs on the UI/insert
  path); a blanket `[...]`/`(...)` regex for non-speech tags was rejected because it could eat
  legitimate dictation — an explicit, expandable literal list is used instead
  (`NonSpeechMarkers` in `DictationSessionController.cs`).

- **Filler-word matching is whole-word and case-insensitive, not substring.** E.g. `um` must not
  match inside `aluminum`. Rationale: avoid corrupting legitimate words while still catching
  filler words and short multi-token phrases like `uh-huh`.

- **Word-replacement rules deliberately deferred.** A clean extension point exists in
  `CleanTranscript` right after filler-word removal for a future custom dictionary/replacement
  feature (see root `ROADMAP.md` item), but it is intentionally not implemented yet to keep the
  post-processing pipeline's initial scope small.

- **Git identity is fixed for this repository.** All commits/pushes use
  `Suresh Kumar Veluswamy <citsuresh@rediffmail.com>` regardless of the local git config, to keep
  authorship consistent. See `.github/copilot-instructions.md`.
