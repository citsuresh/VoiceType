---
mode: agent
description: 'Bootstrap persistent, low-token project memory (CODE_SUMMARY, DESIGN_DECISIONS, PROJECT_STATE, ROADMAP) for any repo/solution, wire it into copilot-instructions.md, and register the docs in solution items.'
---
# Project Memory Bootstrap

Bootstrap persistent project memory for this codebase so future chat sessions can be
resumed cheaply (low token usage) instead of re-exploring the codebase or re-summarizing
conversation history every time.

This prompt is project-agnostic and idempotent: if the target files already exist, review
and update them incrementally instead of overwriting/duplicating content.

## Steps

1. Discover structure: use `get_projects_in_solution` and `get_files_in_project` (or
   equivalent workspace exploration) to enumerate projects, key classes/services, and
   dependencies between components. Do not read every file — focus on structural/entry-point
   types (interfaces, services, view models, main windows/controllers).

2. Create or update `docs/CODE_SUMMARY.md` containing:
   - A short project overview (1-3 sentences: what the app/library does, tech stack, target
     framework).
   - A Mermaid `graph LR` dependency graph of projects/components.
   - A symbol index table (`Symbol | File | Responsibility`) per project, limited to
     structural/entry-point classes and services — not every file.
   - A "Key Flows" section describing 2-5 important end-to-end flows as short arrow chains
     (e.g., `A -> B -> C`).
   - Keep this file concise: prefer tables/graphs over prose.

3. Create or update `docs/DESIGN_DECISIONS.md` (append-only, dated log) seeded with any
   non-obvious architectural/design choices you can infer from the existing code or recent
   changes. Each entry: Decision, Rationale, Alternatives considered (if any). Never delete or
   rewrite prior entries — only append new ones, and if a decision is reversed, add a new
   entry referencing the old one instead of removing it.

4. Create or update `docs/PROJECT_STATE.md`: current focus, open tasks/bugs, recently changed
   files. State explicitly in the file that it is overwritten (not appended) at the end of
   each working session.

5. Create or update `docs/ROADMAP.md`: upcoming planned work as a prioritized checklist
   (e.g., Near-term / Backlog sections). State explicitly that this file is edited
   deliberately when priorities change, not automatically overwritten each session.

6. Update (or create) the repo-scoped `.github/copilot-instructions.md` to add or refresh a
   "Persistent Project Memory" section stating:
   - If it exists, read `docs/CODE_SUMMARY.md` and `docs/DESIGN_DECISIONS.md` before exploring
     the codebase with search tools for a new task. If these files do not exist, fall back to
     normal exploration — their absence is not an error.
   - If it exists, read `docs/PROJECT_STATE.md` and `docs/ROADMAP.md` when the user asks
     "do you remember", references prior work, or asks what's next.
   - Update `docs/CODE_SUMMARY.md` when: a new project is added, a new structural class/service
     is added, a component's responsibility changes, or a project/component dependency changes.
     Do not update for routine bug fixes or small edits that don't affect structure.
   - Update `docs/DESIGN_DECISIONS.md` (append-only, dated entries) when: a non-obvious
     architectural/design choice is made, an alternative approach is rejected with a reason, or
     a past decision is reversed. Never delete prior entries.
   - Update `docs/PROJECT_STATE.md` at the end of a working session to reflect current focus,
     open tasks, and recently changed files (overwrite, not append).
   - Update `docs/ROADMAP.md` only when priorities/plans deliberately change, not automatically
     each session.
   - Keep all four files concise — they exist to reduce token usage on future re-reads, not to
     serve as exhaustive documentation.

7. Register the `docs` folder and its files as a Solution Items folder in the solution file
   (`.slnx`, `.sln`, or equivalent) so they are visible in Solution Explorer:
   - For `.slnx`: add a `<Folder Name="/docs/">` element containing `<File Path="docs/...">`
     entries for each file in the docs folder, placed alongside the existing `<Project>`
     entries.
   - For classic `.sln`: add a solution folder project entry (`Microsoft Visual Studio
     Solution File` "Solution Items" pattern) referencing the same files.
   - Verify the solution still loads/builds after the change (use `run_build` or equivalent).

8. If any of the target files already exist from a prior bootstrap, do not overwrite blindly:
   read them first, then merge/update only what's missing or outdated.

9. Report back a short summary of what was created/updated and confirm the build still
   succeeds.
