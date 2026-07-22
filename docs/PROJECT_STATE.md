# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Implemented the transcript-comparison preview and history feature from
`docs/NEW_FEATURE_SPECS.md`: post-insertion bulb, non-modal comparison popup, token-aware diff
highlighting, and bounded persisted `history.json`. Fixed a broken build (the bulb-recording
methods had been pasted in the middle of `InsertTextOrNotifyAsync`, splitting it in two) and wired
up `TranscriptHistoryService`/`TranscriptPreviewState` in `App.xaml.cs`, which had never been
instantiated. Added a tray "View Transcript History" menu item as the always-available entry point
into history (previously only reachable via the bulb right after a dictation).

## Recently changed files
- `VoiceType/VoiceType/Core/DictationSessionController.cs` — fixed the malformed
  `InsertTextOrNotifyAsync`/`RecordComparisonAndShowBulb` split that broke the build.
- `VoiceType/VoiceType/App.xaml.cs` — instantiated `TranscriptHistoryService` and
  `TranscriptPreviewState` and wired them into the controller; added the tray "View Transcript
  History" callback.
- `VoiceType/VoiceType/UI/TrayIconManager.cs` — added an optional `onViewHistory` callback and
  "View Transcript History" menu item.
- `README.md` — documented the bulb/comparison/history feature.
- `docs/CODE_SUMMARY.md` — added the diff/history/preview/bulb/comparison symbols and flow.

## Open tasks / backlog
- No settings/live-apply hooks exist yet for history retention (e.g. disabling persistence or
  clearing history) — call out in `docs/ROADMAP.md` if desired as a future item.
- Consider adding focused unit tests for `TranscriptDiffService` and `TranscriptHistoryService`
  if/when a test project is introduced.

## Known issues
- None known; `dotnet build` succeeds after the fixes above.
