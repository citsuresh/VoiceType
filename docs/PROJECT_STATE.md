# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Post-processing pipeline for transcripts (roadmap item, now shipped) plus persistent project
memory bootstrap (this `docs/` folder).

## Recently changed files
- `VoiceType/VoiceType/Core/DictationSessionController.cs` — `CleanTranscript` refactored into an
  instance-based, ordered, toggleable pipeline; `NonSpeechMarkers` expanded with `(indistinct)`,
  `[indistinct]`, `(mouse clicking)`, `(mouse click)`, `(clicking)`, `(keyboard clicking)`,
  `(typing)`.
- `VoiceType/VoiceType/Infrastructure/Config/VoiceTypeSettings.cs` — added
  `PostProcessTrimWhitespace`, `PostProcessCollapseSpaces`, `PostProcessCapitalizeSentences`,
  `PostProcessAddTrailingPeriod`, `RemoveFillerWords`, `FillerWords`.
- `VoiceType/VoiceType/appsettings.json` — matching keys/defaults added.
- `VoiceType/VoiceType/UI/Settings/Sections/PostProcessingSection.xaml(.cs)` — new Settings page
  (normalization toggles + editable filler-word list), registered in `SettingsWindow.xaml.cs`.
- `README.md` — new "Post-processing" section documenting the pipeline and marker examples.
- `ROADMAP.md` — removed the shipped "Auto-punctuation / post-processing rules" item; remaining
  items renumbered 1–6.
- `.github/copilot-instructions.md` — created, then compacted to avoid duplicating detail already
  in `README.md`/`ROADMAP.md`.

## Open tasks / backlog
See root `ROADMAP.md` for the full, ordered backlog. Top of list as of this session:
1. Custom word replacements / dictionary (extension point already left in `CleanTranscript`).
2. Streaming (live) transcription polish.
3. Language selection UI (ComboBox instead of free-text).
4. First-run setup / model download helper.
5. Tray-click toggle (hands-free) dictation mode.
6. Installer and auto-start on Windows login *(deferred)*.

## Known issues
None tracked as of this session.
