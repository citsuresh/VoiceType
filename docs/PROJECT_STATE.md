# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Fixed a tray-icon bug reported by the user: double-clicking the tray icon (to open Settings)
would also start a dictation session. Root cause: single-vs-double-click disambiguation relied on
`NotifyIcon.DoubleClick`, a separate OS-raised event that isn't always reliable/timely (e.g. for
tray icons hosted in the Windows 11 "show hidden icons" overflow flyout), racing against a
`DispatcherTimer` used to defer the single-click toggle action. Replaced this with fully
self-contained double-click detection in `TrayIconManager.OnTrayMouseUp`: it now compares the
current `MouseUp` timestamp against the previous one (`SystemInformation.DoubleClickTime`) itself,
with no dependency on the `DoubleClick` event at all. Also added a new feature in the same
handler/session: middle-click on the tray icon now opens the transcript history window (reuses
the existing `_onViewHistory` callback already wired for the "View Transcript History" menu item).
Both changes were committed and pushed to `origin/main` (commit `d62959b`).

## Recently changed files
- `VoiceType/VoiceType/UI/TrayIconManager.cs` — removed `_notifyIcon.DoubleClick` subscription and
  the `_suppressClickUntilUtc`/`OnTrayDoubleClick` race-prone logic; `OnTrayMouseUp` now (1) detects
  double-click itself from `MouseUp` timestamps to open Settings, and (2) handles
  `MouseButtons.Middle` to invoke `_onViewHistory` and open transcript history.

## Open tasks / backlog
- No further roadmap items were addressed this session.

## Known issues
- None known from this session's changes; solution build succeeded after the fix. (Note: while
  the app was actively being debugged/running, a rebuild attempt reported the change wasn't
  applied to the running process — expected, not a bug; requires stopping debugging to pick up
  the new build.)
