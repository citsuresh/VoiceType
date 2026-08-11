# PROJECT_STATE

> Overwritten (not appended) at the end of each working session. Reflects current focus, open
> tasks/bugs, and recently changed files as of the last session.

## Current focus
Ran the `project-memory-management-graph` skill's Begin Session workflow, which detected the
project was synced at stale skill version 4 (current is 9). Ran Bootstrap to re-sync: migrated
"Key Flows" out of `docs/CODE_SUMMARY.md` into a new dedicated `docs/KEY_FLOWS.md` (per the v5
change), and fully regenerated the "Persistent Project Memory" section in
`.github/copilot-instructions.md` (bumped marker to skill-version=9, added
`docs/KEY_FLOWS.md`/`docs/KNOWN_OPEN_FINDINGS.md` references, and added the new "Regression
Auditor Protocol" section per v9). Rebuilt the code knowledge graph fully. No other project code
changes were made this session.

## Recently changed files
- `docs/CODE_SUMMARY.md` — removed the inline "Key Flows" section, replaced with a pointer to
  `docs/KEY_FLOWS.md`.
- `docs/KEY_FLOWS.md` — created; holds the 6 previously-inline flow entries.
- `.github/copilot-instructions.md` — "Persistent Project Memory" section fully regenerated
  (skill-version marker bumped 4 → 9; added Regression Auditor Protocol section).
- `docs/full-graph.json`, `docs/project-dependencies.json` — full rebuild (1042 nodes, 2373 edges).

## Open tasks / backlog
- No roadmap items were addressed this session (see `docs/ROADMAP.md` for actual priorities,
  which is unchanged).

## Known issues
- None new this session.

