# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Polished the transcript-comparison/history feature: added a "Clear History" button to
`ComparisonWindow`, made open comparison/history windows update live (new entries pushed to the
top, no manual refresh needed) and reused an already-open window instead of stacking duplicates
from the bulb/tray. Fixed two real bugs found via log inspection: (1) `TranscriptDiffService`
matched tokens case-insensitively, so capitalization-only changes (e.g. sentence-case) produced
zero highlight spans and silently suppressed the bulb/history entry — case-differing matched
tokens are now emitted as `Modified` spans; (2) `RecordComparisonAndShowBulb` no longer skips
history recording when raw/final text are identical — every dictation is now persisted to
history, while the bulb still only shows when post-processing actually changed the text. Also
removed the `PostProcessAddTrailingPeriod` setting (checkbox, property, appsettings entry, and
code path) after confirming it was a no-op in practice — whisper.cpp already emits its own
terminal punctuation for complete sentences. Added a hardcoded (non-configurable) cleanup in
`ApplySpokenPunctuation` that swallows one stray `?`/`.`/`,` immediately following an
"open/close parenthesis" replacement, since whisper.cpp's punctuation model frequently misfires
right after those phrases. Confirmed there is no whisper.cpp flag to disable punctuation
prediction outright. All changes committed and pushed to `origin/main` (commit `f2c4f2f`).

## Recently changed files
- `VoiceType/VoiceType/UI/ComparisonWindow.xaml(.cs)` — added `ClearHistoryButton` (calls
  `TranscriptHistoryService.ClearAll()` after a confirmation prompt); added static open-window
  tracking (`NotifyNewEntry`, `GetOpenWindow`, `AddEntryOnTop`) so history stays live and windows
  are reused instead of duplicated.
- `VoiceType/VoiceType/Infrastructure/History/TranscriptHistoryService.cs` — added `ClearAll()`.
- `VoiceType/VoiceType/Core/Diff/TranscriptDiffService.cs` — case-differing matched tokens in an
  `Equal` diff op now produce `Modified` highlight spans instead of being silently skipped.
- `VoiceType/VoiceType/Core/DictationSessionController.cs` — `RecordComparisonAndShowBulb` always
  persists a history entry (bulb only shown when text actually changed); notifies open
  `ComparisonWindow`s; reuses an open window on bulb click; removed `AddTrailingPeriod`
  method/call; hardcoded stray-punctuation stripping after parenthesis-phrase replacements.
- `VoiceType/VoiceType/App.xaml.cs` — tray "View Transcript History" reuses an open window instead
  of opening a duplicate; passes `HistoryService` into `ComparisonWindow`.
- `VoiceType/VoiceType/Infrastructure/Config/VoiceTypeSettings.cs`,
  `VoiceType/VoiceType/appsettings.json`,
  `VoiceType/VoiceType/UI/Settings/Sections/NormalizationSection.xaml(.cs)` — removed the
  `PostProcessAddTrailingPeriod` setting entirely.
- `README.md`, `docs/DESIGN_DECISIONS.md` — updated to drop the removed setting from
  documentation and record the design decision.

## Open tasks / backlog
- Consider adding focused unit tests for `TranscriptDiffService` (especially the case-diff and
  parenthesis-punctuation-stripping behavior) and `TranscriptHistoryService.ClearAll()` if/when a
  test project is introduced.
- No settings/live-apply hook exists for disabling history persistence outright (only clearing
  it) — call out in `docs/ROADMAP.md` if desired as a future item.

## Known issues
- None known; `dotnet build` succeeds after the fixes above. Manual verification in the running
  app pending the user's next test pass.
