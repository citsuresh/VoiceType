# VoiceType Roadmap

A running list of ideas and planned features for VoiceType. Nothing here is committed to a
schedule — it's a backlog to capture direction. Items may change, be reordered, or dropped.

Status legend: 🟢 planned · 🟡 idea / needs design · ✅ done

---

## Recently completed

- ✅ Runtime model switching (tray **Model** submenu + Settings window)
- ✅ Settings persistence to `appsettings.json`
- ✅ Whisper server stop/restart on model change (`Server` mode)
- ✅ Microphone selection (`MicrophoneDeviceIndex`)
- ✅ Model-switch status pill ("Switching to …")
- ✅ Blank-audio (`[BLANK_AUDIO]`) handling → "No speech recognized" pill instead of pasting the marker
- ✅ Status pill sizing fix (expands to fit message) and empty model-bubble fix

---

## Planned / ideas

### Settings & input
- 🟢 **Language selection UI** — expose the existing language setting in the Settings window.
- 🟢 **Custom hotkey capture** — click-to-record the hotkey instead of typing modifiers/key by hand.
- 🟡 **Auto-start on Windows login** — optional startup registration with a Settings toggle.

### Transcription quality
- 🟡 **Auto-punctuation / post-processing rules** — configurable cleanup of the transcribed text.
- 🟡 **Custom word replacements / dictionary** — per-user substitutions (names, jargon, acronyms).
- 🟡 **Streaming (live) transcription polish** — refine `Stream` mode UX and partial results.

### Onboarding & distribution
- 🟡 **First-run setup / model download helper** — guide users to download a model on first launch.

---

## Contributing ideas

Add new entries under the appropriate section with a status marker. When an item ships,
move it to **Recently completed** and mark it ✅.
