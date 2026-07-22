# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Implemented roadmap item #8 "Custom removal rules": an independently-toggleable post-processing
step (`EnableCustomRemovalRules` + `CustomRemovalRules` list of `PhraseRemovalRule`) that strips a
user-authored phrase from a sentence's start, end, or anywhere, via a new `RemovalScope` enum.
Added `ApplyCustomRemovalRules` to `DictationSessionController.CleanTranscript`, run after custom
word replacements: splits text into sentences on `.!?`, applies scope-specific regex removal per
enabled rule (longest phrase first), then rejoins and re-collapses whitespace. New
`CustomRemovalRulesSection` Settings page mirrors `CustomWordReplacementsSection`'s UX (DataGrid
with Phrase/Scope columns, per-row enable checkbox, header tri-state "enable all", Add/Edit/Remove
with a scope-picker modal, duplicate/blank validation), registered under the Post-processing nav
category. Seeded `appsettings.json` with the feature disabled and an empty rule list (opt-in,
no built-in phrases, per roadmap's safety note). Verified via a full solution build (0 errors)
after the previously-running `VoiceType.exe` (which was locking the build output) was closed.
Removed the now-shipped item #8 from `docs/ROADMAP.md` (last item, no renumbering needed).

## Recently changed files
- `VoiceType/VoiceType/Infrastructure/Config/VoiceTypeSettings.cs` — added `RemovalScope` enum,
  `PhraseRemovalRule` model, `EnableCustomRemovalRules` (default false), `CustomRemovalRules`
  (default empty list).
- `VoiceType/VoiceType/Core/DictationSessionController.cs` — added `ApplyCustomRemovalRules`
  (sentence-split, scope-aware regex removal); wired into `CleanTranscript` after
  `ApplyCustomWordReplacements`.
- `VoiceType/VoiceType/UI/Settings/Sections/CustomRemovalRulesSection.xaml(.cs)` — new Settings
  page (created this session), including a `RemovalRuleItem` view-model with `ScopeDisplay`.
- `VoiceType/VoiceType/UI/SettingsWindow.xaml.cs` — registered the new section under
  Post-processing (both `_sections` list and nav tree).
- `VoiceType/VoiceType/appsettings.json` — added `EnableCustomRemovalRules`/`CustomRemovalRules`
  defaults (disabled, empty).
- `docs/ROADMAP.md` — removed shipped item #8 (last item in the list; no renumbering needed).

## Open tasks / backlog
- Changes have not yet been committed or pushed — commit/push when the user confirms testing is
  done (per user's global instruction: don't commit/push automatically).
- Consider adding focused unit tests for `ApplyCustomRemovalRules` (start/end/anywhere scope
  matching, multi-sentence input) if/when a test project is introduced.
- No further roadmap items are pending from this session's scope; next priority is whatever the
  user picks from the remaining roadmap items (1–7).

## Known issues
- None known; full solution build succeeds with 0 errors as of this session. An earlier build
  attempt failed only because `VoiceType.exe` was running and locking the output — not a code
  issue.
