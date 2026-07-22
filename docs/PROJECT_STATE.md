# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Specified a transcript-comparison preview and persisted history feature for post-processing.
The feature has not been implemented; `docs/NEW_FEATURE_SPECS.md` is the implementation handoff.

## Recently changed files
- `docs/NEW_FEATURE_SPECS.md` — created the feature specification for the cursor-adjacent preview
  bulb, chat-card comparison popup, semantic diff highlighting, and bounded `history.json` storage.
- `docs/PROJECT_STATE.md` — overwritten for this session snapshot.
- `docs/DESIGN_DECISIONS.md` — appended the persisted semantic-highlight decision.

## Open tasks / backlog
- Implement the transcript-comparison bulb, popup, token-aware diff, and history from
  `docs/NEW_FEATURE_SPECS.md`.
- Define the full history-window entry point, resilient app-data storage behavior, and the precise
  keyboard/focus dismissal integration during implementation.

## Known issues
- No application code changed in this session.
- Build validation could not run because VoiceType was being debugged/running.
