# Copilot Instructions — VoiceType

## Project overview
VoiceType is a local, privacy-preserving Windows dictation app (WPF, .NET 8, C# 12). It has
**no main window** — it runs from the system tray, captures audio, transcribes it locally via
[whisper.cpp](https://github.com/ggml-org/whisper.cpp) (CLI, Server, or Stream mode), cleans up
the transcript, and inserts it at the cursor (clipboard paste or synthetic typing).

## Solution layout
- Solution: `VoiceType/VoiceType.slnx`
- `Core/` — session/state orchestration, notably `DictationSessionController`
  (start/stop dictation, transcript post-processing pipeline, text insertion).
- `Infrastructure/Audio/` — microphone capture (`AudioDataEventArgs`, wave-in wrappers).
- `Infrastructure/Whisper/` — the three transcription backends: `WhisperProcessRunner` (CLI),
  `WhisperServerClient` (long-lived server), `WhisperStreamClient` (real-time streaming).
- `Infrastructure/Config/` — `VoiceTypeSettings` (the persisted settings model) and
  `SettingsLoader` (load/save to `appsettings.json`).
- `Infrastructure/Input/` — `InputInjectionService` for clipboard-paste vs. character-typing insertion.
- `UI/` — tray icon, floating status pill, and the Settings window.
- `UI/Settings/` — `ISettingsSection` contract + `SettingsInput` shared helpers.
- `UI/Settings/Sections/` — one `UserControl` per settings page (e.g. `GeneralSection`,
  `TranscriptionSection`, `DictationSection`, `TextInsertionSection`, `PostProcessingSection`).
  Registered in the `_sections` list in `UI/SettingsWindow.xaml.cs`.

## Conventions specific to this repo
- **Settings model:** every persisted option is a plain property on `VoiceTypeSettings` with an
  inline default and a short `//` comment explaining it. Add the matching key + default to
  `VoiceType/VoiceType/appsettings.json` so the file stays in sync.
- **New settings section:** create a `UserControl` implementing `ISettingsSection`
  (`Title`, `SearchKeywords`, `Load`, `Validate`, `Save`) under `UI/Settings/Sections/`, following
  the style of the existing sections, and register it in `SettingsWindow.xaml.cs`'s `_sections` list.
- **Transcript post-processing:** `DictationSessionController.CleanTranscript` is the single choke
  point before text insertion (see README's "Post-processing" section for user-facing behavior).
  New non-speech tags go in the `NonSpeechMarkers` array (literal match, case-insensitive) rather
  than a blanket regex — a blanket `[...]`/`(...)` pattern could eat legitimate dictation.
- **Live-apply settings:** when adding a setting that should apply without an app restart, wire it
  into the "what changed" diffing block in `SettingsWindow.xaml.cs`'s `SaveButton_Click`.
- Keep the app windowless — do not add a main `Window` shown at startup; UI is tray + floating
  pill + on-demand Settings window only.

## Git commit identity
Commits (and pushes) in this repository must use:
- Author/committer name: `Suresh Kumar Veluswamy`
- Author/committer email: `citsuresh@rediffmail.com`

Example: `git -c user.name="Suresh Kumar Veluswamy" -c user.email="citsuresh@rediffmail.com" commit --author="Suresh Kumar Veluswamy <citsuresh@rediffmail.com>" -m "..."`

## Documentation
- Update `README.md` when user-visible behavior changes; update `docs/ROADMAP.md` (remove + renumber,
  no gaps) when a backlog item ships.

<!-- project-memory-management-graph: skill-version=9 -->
## Persistent Project Memory
Low-token memory files live in `docs/` — read them before re-exploring the codebase or
re-summarizing prior conversation history for a new task:
- `docs/CODE_SUMMARY.md` and `docs/DESIGN_DECISIONS.md` — read before exploring the codebase with
  search tools for a new task. If these files don't exist, fall back to normal exploration; their
  absence is not an error.
- `docs/KEY_FLOWS.md` — traced end-to-end call flows as short symbol arrow-chains. Read alongside
  `docs/CODE_SUMMARY.md`, which just points to it.
- `docs/PROJECT_STATE.md` and `docs/ROADMAP.md` — read when the user asks "do you remember",
  references prior work, or asks what's next. `docs/ROADMAP.md` is the single source of truth
  for planned work.
- `docs/domain-lookup-patterns.md` — read when a task requires domain conventions, naming
  schemes, or business logic that the graph doesn't represent. If this file doesn't exist, that's
  normal (it's only created once a pattern is actually confirmed).
- `docs/KNOWN_OPEN_FINDINGS.md` — a passive, user-controlled list of open/unresolved findings the
  user chose not to act on immediately. Never add, edit, or remove entries without the user
  explicitly asking to; the one exception is flagging a possible recurrence as a suggestion only.
  If this file doesn't exist, that's normal (only created the first time the user explicitly asks
  to add something).
- `docs/full-graph.json` and `docs/project-dependencies.json` — a Roslyn-based code knowledge
  graph, queried via the GraphTools wrapper script (located at
  `C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1`, invoked as
  `Invoke-GraphTools.ps1 -Tool Query -- <args>`), never read wholesale. This is a default, not a
  judgment call: before using a general-purpose search tool (text search, symbol search, grep, or
  similar) to locate a class/interface/enum, find a method's definition, find its callers, find
  its callees, check how two types relate, or otherwise answer "where is X" / "what uses X" for
  any C# symbol — even a simple "find this file/class" request — first check whether
  `docs/full-graph.json` exists, and if so, query it instead:
  ```
  C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- --graph "<repo root>\docs\full-graph.json" --symbol "<fully-qualified-id>"
  C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- --graph "<path>" --symbol "<id>" --direction callers
  C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- --graph "<path>" --symbol "<id>" --direction callees
  C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- --graph "<path>" --list-symbols --project "<project name>"
  ```
  Use `--list-symbols --project` first if the exact symbol ID isn't already known. This
  preference applies even to conceptual/explanatory questions ("explain X", "how does X work") if
  answering requires locating/relating specific C# symbols. Fallback: if `docs/full-graph.json`
  doesn't exist, the wrapper errors or exits non-zero, or the graph doesn't contain an answer
  (e.g. non-code content, file layout), fall back to normal search tools — a missing/failed graph
  query is not a blocking error.

Update them as follows:
- `docs/CODE_SUMMARY.md`: when a new structural class/service is added, a component's
  responsibility changes, or a project/component dependency changes. Not for routine bug fixes.
  Keep the pointer line to `docs/KEY_FLOWS.md` present; do not add Key Flows entries here directly.
- `docs/KEY_FLOWS.md`: when a new end-to-end flow spanning multiple C# symbols is fully traced, add
  it as a short arrow-chain (e.g. `A -> B -> C`).
- `docs/DESIGN_DECISIONS.md`: append-only, dated entries, when a non-obvious architectural/design
  choice is made or reversed. Never delete prior entries.
- `docs/PROJECT_STATE.md`: overwrite (not append) at the end of a working session with current
  focus, open tasks, and recently changed files.
- `docs/ROADMAP.md`: update when priorities or plans deliberately change.
- `docs/domain-lookup-patterns.md`: manually curated; only add/update after the user explicitly
  confirms a domain lookup pattern is worth recording.
- `docs/KNOWN_OPEN_FINDINGS.md`: manually curated by the user only; before adding, check for a
  similar existing entry and ask the user whether it's a recurrence (update in place: last-seen
  date + occurrence count) or a new entry — never decide this on your own.

Keep all memory files concise — they exist to reduce token usage on future re-reads, not to serve
as exhaustive documentation.

## Regression Auditor Protocol
Continuous code-review discipline that applies whenever code changes are made in this project
(see `~/.copilot/skills/project-memory-management-graph/SKILL.md` "Regression Auditor Protocol"
section for full detail): propose a breakdown of non-trivial multi-part changes before writing
code (Pre-Build Decomposition) and confirm it with the user; after each meaningful part and once
more after completion, run an independent Regression Auditor subagent reviewing the diff alone
(no builder rationale passed in) for regressions/contradictions/duplication, running
build/tests where possible, and classifying every finding as out-of-scope or "expected in Part N";
out-of-scope findings are reported only (never touched), with unrelated confirmed issues offered
as `docs/KNOWN_OPEN_FINDINGS.md` candidates (same explicit-confirmation rule as above); in-scope
fixes are proposed as diffs only, never auto-applied. If the same issue recurs across 2+ fix
attempts, stop and recommend a fresh context instead of continuing to iterate. Anchor quality
judgments to existing conventions, written acceptance criteria, or passing tests — never
open-ended opinion — and never use confidence language ("robust", "production-ready", etc.) in
audit reports. Always show the full Review Report; never apply a fix without explicit approval.
The user may suspend both Pre-Build Decomposition and the Regression Audit together for the
current session only (e.g. "skip regression review for this session," POC/throwaway work); this
never persists past the session and must be re-requested each time.

## Project Guidelines
- Manual commit review before any commit.
- Build/test verification after every change.
- Do not commit or push automatically — wait for explicit user confirmation first.

## External Project Exploration
When exploring external projects for VoiceType, use them only as conceptual references; do not copy their code, and record relevant repository references in the roadmap where useful.

## Build & verify
- Build via the solution `VoiceType/VoiceType.slnx` (or `dotnet build`).
- The app must not be running when rebuilding, or the `Copy`/link step fails with a file-lock error
  (`VoiceType.exe` in use) — exit the tray app first.

## File Editing Instructions
- If a file edit tool fails to edit a file, ask the user to either close the file or reopen/restart Visual Studio so the edit can be retried, before falling back to other editing methods.

## Response Guidelines
- Keep responses minimal — short phrases only, no full sentences, just essential facts/actions.
- Keep replies concise and minimal by default (no filler, no restating the question, no
  unnecessary preamble), EXCEPT in these cases where full detail is required:
  - Design rationale discussions.
  - Build/error diagnosis.
  - Before any destructive action.
  - When multiple approaches exist.
  - When generating docs/*.md content itself.
