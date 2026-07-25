# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Ran the `project-memory-management` skill's Initialize and Bootstrap workflows while testing the
skill itself (files under `.github/` and `docs/` were deliberately deleted/reset multiple times
during testing). Along the way, found and fixed a real mistake: when `.github/copilot-instructions.md`
had been deleted, it was recreated from a generic template instead of the actual committed
version, silently dropping real project-specific sections ("External Project Exploration",
"Build & verify", "File Editing Instructions", "Git commit identity", etc.). Restored the file via
`git checkout HEAD -- .github/copilot-instructions.md`, then merge-safe-added only what the skill's
Bootstrap steps 7/8 require (a "Project Guidelines" section with the 2 mandated bullets, and an
extension to the existing "Response Guidelines" section with the required exception list) without
touching any other existing content. Also updated
`~/.copilot/skills/project-memory-management/SKILL.md` to require resolving the repo root to an
absolute path (`git rev-parse --show-toplevel`) before any file operation, after an earlier mistake
where memory files were written to the nested solution subfolder (`VoiceType\VoiceType\.github` /
`...\docs`) instead of the true repo root.

## Recently changed files
- `.github/copilot-instructions.md` — restored from git HEAD (undoing an earlier accidental
  generic-template overwrite), then merge-safe-extended with a "Project Guidelines" section and
  additions to "Response Guidelines" per the skill's Bootstrap steps.
- `.github/prompts/bootstrap.prompt.md`, `.github/prompts/end-session.prompt.md` — created/moved
  to the true repo root (`C:\MyFiles\Git\VoiceType\.github\prompts\`) via the Initialize workflow.
- `~/.copilot/skills/project-memory-management/SKILL.md` — added absolute-path repo-root
  resolution requirement to the Path resolution section.
- `docs/CODE_SUMMARY.md`, `docs/DESIGN_DECISIONS.md`, `docs/ROADMAP.md` — confirmed intact/current
  (restored via git during this session's testing; no structural changes to merge in).

## Open tasks / backlog
- No roadmap items were addressed this session (see `docs/ROADMAP.md` for actual priorities,
  which is unchanged).

## Known issues
- `run_build` failed once this session with MSB3021/MSB3027 (file lock on `VoiceType.exe`)
  because the app was running at the time — not a code regression; re-run after stopping the
  running instance.
