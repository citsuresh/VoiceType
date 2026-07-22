# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
TreeView navigation is implemented for the Settings window. The existing settings pages remain
selectable leaf nodes, while the navigation model now supports expandable parent categories for
future settings sub-pages.

## Recently changed files
- `VoiceType/VoiceType/UI/SettingsWindow.xaml` — replaced the flat settings `ListBox` with a
  searchable `TreeView`.
- `VoiceType/VoiceType/UI/SettingsWindow.xaml.cs` — added recursive tree filtering, selected-page
  preservation, and safe handling for non-selectable parent categories.
- `VoiceType/VoiceType/UI/Settings/NavNode.cs` — added the hierarchy-aware navigation node model.
- `VoiceType/ROADMAP.md` — removed the shipped TreeView backlog item and renumbered the remaining
  items.
- `VoiceType/docs/PROJECT_STATE.md` — overwritten for this session snapshot.
- `VoiceType/docs/DESIGN_DECISIONS.md` — appended the TreeView hierarchy decision.
- `VoiceType/docs/CODE_SUMMARY.md` — documented the `NavNode` navigation model.

## Open tasks / backlog
See root `ROADMAP.md` for the full, ordered backlog. The next relevant work is independently
configurable post-processing settings and rules, with future child pages under `Post-processing`.

## Known issues
- Parent category nodes are intentionally non-selectable; selecting one keeps the current details
  page visible.
- Additional post-processing settings and rule application are not implemented.
- The concise-prompt transformation provider is not implemented.
