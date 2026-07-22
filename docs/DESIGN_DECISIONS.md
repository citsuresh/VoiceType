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
  sentences, remove filler words) are individually toggleable via settings, in a fixed code order.
  Rationale: keep behavior deterministic and cheap (runs on the UI/insert path); a blanket
  `[...]`/`(...)` regex for non-speech tags was rejected because it could eat legitimate dictation
  — an explicit, expandable literal list is used instead (`NonSpeechMarkers` in
  `DictationSessionController.cs`). A "trailing period" auto-add setting was removed after
  discovering whisper.cpp already emits its own terminal punctuation for complete sentences,
  making the setting a no-op in practice.

- **Filler-word matching is whole-word and case-insensitive, not substring.** E.g. `um` must not
  match inside `aluminum`. Rationale: avoid corrupting legitimate words while still catching
  filler words and short multi-token phrases like `uh-huh`.

- **Word-replacement rules deliberately deferred.** A clean extension point exists in
  `CleanTranscript` right after filler-word removal for a future custom dictionary/replacement
	feature (see `docs/ROADMAP.md` item), but it is intentionally not implemented yet to keep the
  post-processing pipeline's initial scope small.

- **Git identity is fixed for this repository.** All commits/pushes use
  `Suresh Kumar Veluswamy <citsuresh@rediffmail.com>` regardless of the local git config, to keep
  authorship consistent. See `.github/copilot-instructions.md`.

## 2026

- **2026-07-22 — Persist transcript comparison highlights as semantic spans.** Transcript history
  stores the exact raw Whisper and final post-processed strings plus offset/length spans marked
  `removed`, `modified`, or `added`; the UI maps these semantic kinds to its red/yellow/green
  presentation. Rationale: avoids ambiguity and escaping requirements of inline bracket markup
  when dictation legitimately contains parentheses, brackets, code, or punctuation, while keeping
  persisted entries independent of WPF colors and controls. Alternatives considered: persisting
  WPF rich-text formatting, storing every rendered token, and embedding marker syntax in text;
  all were rejected as brittle, presentation-coupled, or unnecessarily complex.

- **2026-07-22 — Defer global TreeView migration while separating post-processing rule categories.**
  The current implementation should retain the existing flat settings ListBox and add the
  post-processing categories as separate right-side GroupBoxes; a later navigation enhancement
  can migrate the full settings shell to a hierarchical TreeView. Additional rule-based settings
  will remain separate categories for normalization, filler words, spoken punctuation, custom word
  replacements, and initially empty `CustomRemovalRules`. Rationale: this keeps the immediate
  post-processing work compatible with the current `ISettingsSection` architecture while avoiding
  a large settings-page form. Alternatives considered: migrate the whole navigation immediately,
  or place every rule list in one undifferentiated dictionary; both were rejected in favor of a
  staged navigation change and explicit rule semantics.

- **2026-07-22 — Settings navigation uses a hierarchy-aware wrapper without changing `ISettingsSection`.**
  `NavNode` represents a title, optional selectable section, and child nodes; `SettingsWindow`
  recursively filters and displays those nodes in a `TreeView`. Parent category nodes deliberately
  have no section and do not replace the current detail page when selected. Rationale: preserves
  the existing single Save/Cancel transaction and page contract while allowing future
  `Post-processing` child pages. Alternatives considered: extend `ISettingsSection` with hierarchy
  metadata or create selectable summary pages for every parent; both were unnecessary for the
  current navigation scope.

- **2026-07-23 — Tray "View Transcript History" as the always-available history entry point.**
  The comparison popup was only reachable by clicking the post-insertion bulb, so history built up
  silently with no way to browse it outside that narrow window right after a dictation. Added an
  optional `onViewHistory` callback to `TrayIconManager` that opens `ComparisonWindow` pre-loaded
  with all persisted entries. Rationale: reuses the existing chat-card UI and `TranscriptHistoryService`
  without new windows or view models; keeps the tray as the single control-center entry point.
  Alternatives considered: a dedicated Settings section for history (deferred — no settings are
  needed to simply view history) and auto-showing history on startup (rejected as intrusive for a
  windowless app).

- **2026-07-22 — History is always persisted; the bulb is shown only when text actually
  changed.** `RecordComparisonAndShowBulb` used to skip persisting a `ComparisonEntry` entirely
  when raw and final text were identical, so unmodified dictations never appeared in history.
  Changed to always persist an entry (with empty highlight spans when nothing changed) and notify
  any open `ComparisonWindow` immediately, while still only showing the post-insertion bulb when
  post-processing actually altered the text. Rationale: history should be a complete record of
  what was dictated, not just a diff log; the bulb remains change-triggered since there is nothing
  meaningful to compare otherwise.

- **2026-07-22 — Case-only token differences are treated as `Modified`, not silently equal.**
  `TranscriptDiffService.ComputeDiffOps` matches tokens with `OrdinalIgnoreCase` (so token
  alignment is robust to capitalization), but this meant a sentence-capitalization change (e.g.
  `interesting` → `Interesting` from `CapitalizeSentences`) produced zero highlight spans and,
  combined with the previous history-skip behavior, silently suppressed both the bulb and the
  history entry. Matched-but-case-differing tokens now emit a `Modified` highlight span on both
  sides. Alternatives considered: switching the LCS match to `Ordinal` (rejected — would fragment
  otherwise-identical runs into spurious delete/insert pairs for every capitalization change).

- **2026-07-22 — Hardcoded (non-configurable) stray-punctuation stripping after parenthesis
  replacements.** whisper.cpp's punctuation model frequently emits a spurious `?`/`.`/`,`
  immediately after a spoken "open parenthesis"/"close parenthesis" phrase (an ASR artifact, not
  something the user said). `ApplySpokenPunctuation` now swallows one such trailing mark, but only
  for the two parenthesis rules specifically, and only as a hardcoded pattern extension — not a
  user-facing setting, since this is a narrow, deterministic ASR-artifact cleanup rather than a
  general punctuation preference.

