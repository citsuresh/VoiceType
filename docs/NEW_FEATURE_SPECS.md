# New Feature Specification: Transcript Comparison Preview and History

## Goal

Make post-processing changes visible without interrupting dictation. After VoiceType inserts processed text, users can inspect the original Whisper output alongside the final inserted text and see exactly what changed.

## Scope

This specification covers:

- A cursor-adjacent transcript-comparison bulb after insertion.
- A non-modal comparison popup.
- Word/token-level diff highlighting.
- Bounded persisted transcript history.

It does not include a pre-insertion confirmation dialog. That may be added later as an optional feature.

## Terminology

- **You spoke**: the raw final Whisper output, before `CleanTranscript` post-processing.
- **Final text**: the output of `CleanTranscript`, which is the text inserted into the target application.
- **Comparison entry**: one pair of You spoke and Final text values, their highlights, and metadata.

## Post-insertion bulb

After a successful insertion, VoiceType should create a small yellow bulb or balloon near the current mouse cursor when You spoke and Final text differ.

### Behavior

- Position it near the cursor, constrained within the active screen work area.
- Do not activate the bulb or take focus from the target application.
- Keep it visible until either:
  - the user starts typing meaningful text in the target application; or
  - the foreground/focus target changes.
- Do not use a fixed auto-close timeout.
- Clicking the bulb opens the comparison popup for the most recent entry.
- Create it as a separate overlay window from `BreathingOverlayWindow`, which is intentionally click-through and cannot be clicked.

## Comparison popup

The comparison popup is a non-modal, floating, focusable window. It must not be a pre-insertion review dialog.

### Behavior

- Open when the user clicks the bulb.
- Include a close button.
- Close on `Esc`.
- Support selecting and copying complete or partial text.
- Display a chat-card-style group for a comparison entry:
  - a **You spoke** card containing the raw Whisper text;
  - a **Final text** card containing the processed text;
  - optional timestamp and model metadata.
- Keep unchanged text plain within its card.
- Highlight only individual changed tokens with compact rounded color boxes.

## Diff highlighting

Use a word/token-aware diff rather than a character-by-character diff.

### Colors

| Semantic kind | Where shown | Visual treatment |
|---|---|---|
| Removed | You spoke only | Red boxed token |
| Modified | You spoke and Final text | Yellow boxed token |
| Added | Final text only | Green boxed token |
| Unchanged | Both cards | Normal card text |

A paired deletion and insertion that represent a replacement or normalization should be rendered as **Modified** in both cards where appropriate, rather than always as separate removed and added tokens.

### Example

Given:

- You spoke: `um this is a test comma okay`
- Final text: `This is a test, okay.`

The intended diff is:

- `um` is removed and rendered as a red token in You spoke.
- `this` and `This` are modified and rendered as yellow tokens in their respective cards.
- `comma` and `,` are modified and rendered as yellow tokens in their respective cards.
- `.` is added and rendered as a green token in Final text.

## History

Provide a graphical card/chat-style history using the same comparison cards and highlights as the popup.

### Initial retention policy

- Persist history to `history.json`.
- Start with a bounded history of 50 entries.
- Entries contain sensitive dictated text; keep privacy in mind when selecting the storage location and when designing future settings.
- Cross-restart persistence is part of the initial requirement because `history.json` is persisted. A future setting may allow users to disable persistence or clear history.

## Persistence format

Do not persist WPF rich-text objects, brushes, literal colors, or bracket-based inline markup. Dictated text may legitimately include brackets or parentheses, so marker syntax is unsafe and would require escaping/parsing.

Persist exact source strings and semantic highlight spans. The UI maps semantic highlight kinds to current theme brushes.

```json
{
  "version": 1,
  "entries": [
	{
	  "id": "4d2c4b7b-6a42-4fc6-b9c3-3aefca6d7a2c",
	  "createdUtc": "2026-08-01T10:15:30.123Z",
	  "spokenText": "um this is a test comma okay",
	  "finalText": "This is a test, okay.",
	  "spokenHighlights": [
		{ "start": 0, "length": 2, "kind": "removed" },
		{ "start": 3, "length": 4, "kind": "modified" },
		{ "start": 18, "length": 5, "kind": "modified" }
	  ],
	  "finalHighlights": [
		{ "start": 0, "length": 4, "kind": "modified" },
		{ "start": 14, "length": 1, "kind": "modified" },
		{ "start": 20, "length": 1, "kind": "added" }
	  ]
	}
  ]
}
```

A top-level `version` field supports future format changes.

## Implementation constraints

- Capture raw Whisper output and final post-processed output at the existing `CleanTranscript` and insertion flow. Do not duplicate or apply post-processing twice.
- Preserve existing focus-safe insertion behavior.
- Reuse the existing keyboard and foreground-window infrastructure where practical to dismiss the bulb after typing or focus change.
- Extract reusable models/services for diff generation, highlight spans, history persistence/capping, and active preview state.
- Render highlighted spans using a WPF approach that supports selecting and copying text.
- Add focused tests when a suitable test project exists; otherwise structure the diff and history logic for independent testing.

## Documentation and configuration

- Update `README.md` when the feature ships.
- If settings are added, add matching defaults to `appsettings.json`, inline comments to `VoiceTypeSettings`, settings UI support, and live-apply handling where applicable.
- Update `docs/CODE_SUMMARY.md` if new structural services/components are introduced.
- Append a dated entry to `docs/DESIGN_DECISIONS.md` for non-obvious design choices.
- Update `docs/PROJECT_STATE.md` at the end of the implementation session.
