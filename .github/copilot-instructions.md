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

<!-- project-memory-management-graph: skill-version=23 -->
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
- `docs/FEATURE_CHECKLIST.md` — read when planning or adding a feature, if it exists; it is
  user-curated and must not be changed without explicit instruction.
- `docs/SCRIPTS.md` — check before writing code for a repeatable mechanical step, if it exists.

Registered solution graphs (never read wholesale) are queried with the GraphTools wrapper at
`C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1`, invoked as
`C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- <args>`:

| Solution path relative to repo root | Slug | Graph file | Dependencies file |
| --- | --- | --- | --- |
| `VoiceType\VoiceType.slnx` | `voicetype` | `docs/full-graph.voicetype.json` | `docs/project-dependencies.voicetype.json` |

Before general-purpose search for a C# class/interface/enum, method definition, caller/callee,
type relationship, or "where is X" / "what uses X", query the registered graph first — also for
conceptual questions when specific C# symbols must be located or related. Query the IDE solution
first only when it can be determined from context; otherwise search registered graphs and label
results by solution, noting symbols found in multiple solutions. Use `--list-symbols --project` to
discover an unknown symbol ID. Query templates:
```
C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- --graph "<repo root>\docs\full-graph.voicetype.json" --symbol "<fully-qualified-id>"
C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- --graph "<path>" --symbol "<id>" --direction callers
C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- --graph "<path>" --symbol "<id>" --direction callees
C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- --graph "<path>" --list-symbols --project "<project name>"
```
If no registered graph exists, a graph file is missing, a query fails, or no graph contains the
answer, state that explicitly (include full errors); a symbol-not-found result may indicate stale
data. Then fall back to normal search. At Begin Session, compare each dependencies file's
`CommitSha` with `git rev-parse HEAD`; ask whether to Bootstrap only on mismatch.

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

Reusable scripts: before writing code for a repeatable mechanical step (filtering a log, applying the same edit to many files, extracting data from several files), check `docs/SCRIPTS.md` if it exists and run a script that fits, as its index line says; if the index line says it modifies or deletes files, ask the user before each run, and check its output before relying on it. If you wrote working code for such a step that is likely to be reused with different inputs, is more than a one-liner, can take its inputs as parameters, and contains no credentials, tokens, customer data or personal details, save it as `scripts/agent/<verb-noun>.ps1` with a header comment (purpose, parameters, whether it modifies anything), add one line to `docs/SCRIPTS.md` (table: script | purpose | how to call | read-only or modifies | date; create the file with the table header if it is missing), and say so in your reply. Before saving, check whether an existing script in `scripts/agent/` can take the new case as a parameter. If one can, extend that script with a backward-compatible parameter (its default keeps the current behaviour, and existing parameter names, order and output format stay unchanged), update its header comment, index line and date, and say so, rather than saving a near-copy. If the task's file rules forbid changing the existing script, do not save a near-copy; say in your reply which parameter the existing script needs. Every part that varies between uses (paths, patterns, comment markers, row counts, exclusions) must be a parameter. For C# projects, skip `obj` and `bin` folders by default, with a switch to include them. Before finishing a task, check once whether you wrote any script worth saving. If a script fails or is out of date, fix it in place and update its date, or do the step normally; never loop on a broken script. When a task runs a listed script, set its date in `docs/SCRIPTS.md` to today if the task's file rules allow editing that file; otherwise leave the date unchanged and say so in your reply. The user curates these files: never delete scripts or index lines on your own.

## Regression Auditor Protocol
For code changes, propose the smallest independently reviewable breakdown and wait for
confirmation before any non-trivial multi-part implementation. Include `docs/FEATURE_CHECKLIST.md`
items if it exists. After each meaningful part and once at completion, have an independent
Regression Auditor review the diff without builder rationale: check unintended behavior, callers,
shared state, contradictions, duplication, and run build/tests where available. Classify findings
as out-of-scope or expected in a named Part. Out-of-scope findings are report-only; do not
investigate or fix them, and mention `docs/KNOWN_OPEN_FINDINGS.md` only as a candidate subject to
explicit user direction. In-scope findings need root cause, effort/risk estimate, and a proposed
diff only. The final audit also checks full build/tests, docs synchronization (`PROJECT_STATE`,
`CODE_SUMMARY`, `DESIGN_DECISIONS`, `ROADMAP` where present), and cross-cutting consistency.
Show the full Review Report and stop; never apply a proposed fix without the user's explicit,
individual approval. Plan approval does not approve audit fixes. If the same issue recurs across
two or more fix attempts, stop and recommend a fresh focused context.
Anchor judgments to repository conventions, written acceptance criteria, or tests; do not use
confidence language in reports. A session-only request to skip review suspends both audit and
breakdown; clarify a request that targets only one. Reduced Cadence Mode requires an explicit
request and confirmed phases: audit at each phase boundary, revert to full per-part cadence after
a real in-scope finding, and use the last phase audit as the final audit. Modes are session-scoped
unless explicitly made persistent.

## Project Guidelines
- Manual commit review before any commit.
- Build/test verification after every change.
- Do not commit or push automatically — wait for explicit user confirmation first.
- This project has a WPF/WinForms UI. Use the `vs-debug` skill for Visual Studio debugging; use `ui-navigation-orchestrator` to navigate the running app to a screen or feature, including during debugging.

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
