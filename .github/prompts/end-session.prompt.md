---
mode: agent
description: 'Cheap end-of-session snapshot: update docs/PROJECT_STATE.md (and DESIGN_DECISIONS.md/CODE_SUMMARY.md only if genuinely needed) without re-scanning the whole codebase.'
---
# End Session

Cheap companion to `bootstrap-project-memory`. Run this at the end of a chat session to
snapshot what happened, using only the context already gathered in this session — do NOT
re-scan the whole codebase structure (no `get_projects_in_solution`/`get_files_in_project`
sweep). This keeps the closing update low-token.

## Steps

1. Overwrite `docs/PROJECT_STATE.md` (not append) with, based only on this session's context:
   - Current Focus: what was worked on in this session.
   - Open Tasks / Known Issues: anything left unresolved or explicitly deferred.
   - Recently Changed Files: files created/modified in this session.
   - If `docs/PROJECT_STATE.md` does not exist, skip silently (do not create the full docs set —
     that is `bootstrap-project-memory`'s job).

2. Only if a genuinely non-obvious design/architecture decision was made in this session and is
   not already captured, append one dated entry to `docs/DESIGN_DECISIONS.md` (Decision,
   Rationale, Alternatives considered). Skip this step entirely if no such decision occurred, or
   if the file does not exist.

3. Only if a structural change occurred in this session (new project, new structural
   class/service, changed responsibility, changed project dependency) and is not already
   reflected, update the relevant section of `docs/CODE_SUMMARY.md` inline. Do not re-derive the
   whole file or re-scan unrelated parts of the codebase. Skip if the file does not exist or if
   no structural change occurred.

4. Do not modify `docs/ROADMAP.md` unless the user explicitly discussed a priority/plan change in
   this session.

5. Report back a short (2-5 line) summary of what was updated (or confirm nothing needed
   updating).
