# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Designing the next settings and transcript post-processing work. The current sequence is to
implement TreeView settings navigation first, followed by additional rule-based post-processing.
The rule-based design intentionally excludes semantic understanding and LLMs.

## Recently changed files
- `VoiceType/VoiceType/Infrastructure/Config/VoiceTypeSettings.cs` — expanded the default
  non-lexical filler list with `uhm`, `erm`, `mhm`, `uh huh`, and `mm-hmm`.
- `VoiceType/VoiceType/appsettings.json` — synchronized the additional filler-word defaults.
- `VoiceType/ROADMAP.md` — added roadmap items for local concise-prompt transformation, future
  TreeView settings navigation, and additional post-processing settings.
- `VoiceType/docs/FEATURE_COMPARISON.md` — added the feature comparison and local transformation
  discussion.
- `VoiceType/docs/PROJECT_STATE.md` — overwritten for this session's snapshot.
- `VoiceType/docs/DESIGN_DECISIONS.md` — appended the settings-navigation and post-processing
  design decision.

## Open tasks / backlog
See root `ROADMAP.md` for the full, ordered backlog. Current planned sequence:
1. Implement TreeView navigation for the Settings window.
2. Add independently configurable post-processing categories: normalization, filler words,
   spoken punctuation, custom word replacements, and `CustomRemovalRules`.
3. Keep `CustomRemovalRules` empty by default; users define phrase and sentence scope themselves.
4. Keep semantic concise-prompt transformation and any `flan-t5-small` work separate from the
   rule-based post-processing implementation.
5. Consider TreeView migration before implementing the additional post-processing pages.

## Known issues
- The current settings UI still uses a flat ListBox; TreeView navigation is planned but not yet
  implemented.
- The additional post-processing settings and rule application are planned but not implemented.
- The concise-prompt transformation provider is planned but not implemented.
