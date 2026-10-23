# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Ran Begin Session after confirming the v9 marker was stale against v23. Rebuilt the
`VoiceType\VoiceType.slnx` graph under the `voicetype` registry name (1,042 nodes, 2,373 edges;
18.11 seconds), refreshed `.github/copilot-instructions.md`, and verified its reusable-scripts
bullet with an exact string comparison. The graph matches current commit
`322d58a8477d0689f66973250a1aa04c6cddcbf2`. At the user's direction, removed the legacy
`docs/full-graph.json`; the solution's old graph/dependencies references were left unchanged.

Then fixed hold-to-talk never starting on Ctrl+Space (pill flashed and closed): the hook swallows
the Space key, so `GetAsyncKeyState` never saw it held, and `IsHotkeyPhysicallyDown` aborted every
start as a fast release. It now uses the hook-tracked `_suppressingTarget` flag. Compiles (temp
output dir); Regression Auditor found nothing. Not yet runtime-tested; not committed.

## Recently changed files
- `docs/ROADMAP.md` — item 1 expanded (Whisper streaming root cause, Options A/B/C); new item 2
  (sherpa-onnx engine: Parakeet + streaming Zipformer, comparison, setups, preview/final UX);
  items renumbered 3–7.
- `VoiceType/VoiceType/Infrastructure/Hotkeys/GlobalHotkeyManager.cs` — `IsHotkeyPhysicallyDown`
  uses `_suppressingTarget` instead of `GetAsyncKeyState` for the target key.
- `.github/copilot-instructions.md` — marker v23, per-solution graph registry, updated script and
  auditor guidance, WPF/WinForms debugging reference.
- `docs/full-graph.voicetype.json`, `docs/project-dependencies.voicetype.json` — full graph rebuild.
- `docs/full-graph.json` — deleted at the user's request.
- `docs/project-dependencies.json` — replaced by the registered slug-named file.
- `docs/PROJECT_STATE.md` — this session snapshot.

## Open tasks / backlog
- `VoiceType.slnx` still references the removed unqualified graph/dependencies paths; the user
  chose to leave these solution-folder references unchanged for now.
- No roadmap items were addressed; `docs/ROADMAP.md` is unchanged.

## Known issues
- Normal build output not refreshed: `VoiceType.exe` is locked by running PID 101020. Exit the
  app, rebuild, then test Ctrl+Space hold and quick tap.
- whisper-server failed to bind 127.0.0.1:51234 after resume (fell back to CLI); not investigated.

