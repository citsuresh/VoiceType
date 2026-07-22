# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Implemented roadmap item "Custom word replacements / dictionary" (removed from the roadmap as
shipped, remaining items renumbered). Added an independently-toggleable post-processing step
(`EnableCustomWordReplacements` + `CustomWordReplacements` list) that replaces mis-heard
words/phrases (e.g. "co pilot" → "Copilot") with a user-authored correction: whole-word/whole-
phrase, case-insensitive matching, longest-phrase-first, replacement always inserted exactly as
typed (no case-preservation). New `CustomWordReplacementsSection` Settings page mirrors
`SpokenPunctuationSection`'s UX (DataGrid with a per-row enable checkbox, header tri-state
"enable all", Add/Edit/Remove, duplicate/blank validation), registered under the
Post-processing nav category. Seeded the user's own `appsettings.json` with rules mapping
"co pilot"/"copilot"/"copy lot"/"co-pilot" → "Copilot" to fix a specific mis-hearing issue.
Updated README's post-processing table and docs/ROADMAP.md (item removed, remaining items
renumbered 1–8, and the old "Additional post-processing settings" item trimmed down to just the
still-open "custom removal rules" scope). Verified via `dotnet build --no-incremental` (0
errors) — the VS-integrated build tool reported a stale in-IDE designer-cache error for the new
XAML file that did not reproduce via the CLI. All changes committed locally to `main` (commit
`8dc6def`, author identity per repo convention) but **not yet pushed**.

## Recently changed files
- `VoiceType/VoiceType/Infrastructure/Config/VoiceTypeSettings.cs` — added `WordReplacementRule`
  model, `EnableCustomWordReplacements` (default false), `CustomWordReplacements` (default empty
  list).
- `VoiceType/VoiceType/Core/DictationSessionController.cs` — added `ApplyCustomWordReplacements`
  (whole-word/phrase, case-insensitive, longest-first regex replace); wired into `CleanTranscript`
  after filler-word removal, replacing the old roadmap-item-#2 extension-point comment.
- `VoiceType/VoiceType/UI/Settings/Sections/CustomWordReplacementsSection.xaml(.cs)` — new
  Settings page (created this session).
- `VoiceType/VoiceType/UI/SettingsWindow.xaml.cs` — registered the new section under
  Post-processing.
- `VoiceType/VoiceType/appsettings.json` — added `EnableCustomWordReplacements`/
  `CustomWordReplacements` defaults; user's local copy also seeded with live "Copilot"
  mis-hearing fixes (enabled).
- `README.md`, `docs/ROADMAP.md` — documented the new step; removed the shipped roadmap item and
  renumbered/trimmed the rest.

## Open tasks / backlog
- User's changes are committed but not pushed to `origin/main` — push when ready.
- Confirm in the running app whether "co pilot"/"copy lot"/"co-pilot" cover all of whisper's
  actual mis-hearings of "Copilot"; add more variants via the Settings UI if not.
- Remaining roadmap item #8 ("Custom removal rules": phrase removal with start/end/anywhere
  sentence scope) is still unimplemented — natural follow-on to this session's work.
- Consider adding focused unit tests for `ApplyCustomWordReplacements` (and the older
  `TranscriptDiffService`/`TranscriptHistoryService.ClearAll()`) if/when a test project is
  introduced.

## Known issues
- None known; `dotnet build`/`dotnet build --no-incremental` succeed with 0 errors. The VS
  build-tool integration showed a stale designer-cache XLS0414 error for the new
  `CustomWordReplacementsSection.xaml` immediately after file creation — expected to clear on a
  VS reload; not a real compile error.
