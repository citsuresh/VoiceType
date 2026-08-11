# KEY_FLOWS

> Traced end-to-end call flows as short symbol arrow-chains. Migrated out of `docs/CODE_SUMMARY.md`
> so that file stays a structural map. Update when a new end-to-end flow spanning multiple C#
> symbols is fully traced and confirmed.

- **Hold-to-talk dictation:** `GlobalHotkeyManager` (key down) → `DictationSessionController` starts session → `AudioCaptureService` records → (key up) → transcribe via `WhisperServerClient`/`WhisperProcessRunner`/`WhisperStreamClient` → `CleanTranscript` pipeline → `InputInjectionService` inserts text.
- **Toggle (hands-free) dictation:** Tray single-click or toggle hotkey → same session/transcribe/insert path as above, plus optional idle auto-stop.
- **Settings save:** `SettingsWindow.SaveButton_Click` → `Validate()` + `Save()` across all `ISettingsSection`s → `SettingsLoader.SaveAsync` → diff old/new values → live
- **Transcript comparison:** after a successful insertion, `DictationSessionController.RecordComparisonAndShowBulb` diffs raw vs. final text via `TranscriptDiffService`, appends a `ComparisonEntry` to `TranscriptHistoryService` (persisted to `history.json`), publishes it to `TranscriptPreviewState`, and shows `TranscriptBulbWindow` near the cursor; clicking the bulb (or the tray's "View Transcript History") opens `ComparisonWindow`.
- **Model switch (tray menu):** `TrayIconManager` menu click → `WhisperProcessRunner.EnumerateModels()` list → update `VoiceTypeSettings.WhisperModelPath` → `WhisperServerClient` restarts (Server mode) or picked up next dictation (Cli/Stream).
- **Text insertion fallback:** `FocusedControlInspector` checks focus → if not editable, `CopyToClipboardWhenNoEditable` copies text to clipboard + optional notification instead of typing/pasting.
