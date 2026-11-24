# Repository instructions

## Start tidy, stop tidy

Work in this repository may come from several agent tools, and sessions can end partway through a task.

- **Check first.** Before starting new work, run `git fetch --prune` and check `git worktree list`, local and remote branches, and open PRs. Finish, merge, or park leftover work instead of starting a parallel attempt. Squash merges hide merged work from ancestry, so check PR state, not only `git branch --merged`.
- **Checkpoint and hand off.** Commit and push at meaningful checkpoints. Before stopping, leave a short handoff note (in the PR or commit message) saying what is done and what is next.
- **Keep the tree clean.** Test fixtures, scratch scripts, logs, and generated output go only in the designated test or temp locations or the gitignored `.agent/` directory. Never leave throwaway test files elsewhere in the tree, and remove scratch artifacts before finishing.
- **Keep branches short-lived.** Merge promptly and delete branches after merge. Keep GitHub current: push finished work, update or close PRs, and close finished issues.
- **Goal state.** One `main` with CI passing. Never leave `main` red or work stranded on side branches.
- **Remove only with evidence.** Remove a worktree or branch only once its work is merged, contained in `main`, or pushed to the remote. Never use `--force` to remove one; report anything uncertain instead of deleting it. Run `git worktree prune` for entries whose directories are gone.
- **Ask when unclear.** If it is unclear whether leftover work should be finished, kept, or removed, or what the intended state is, stop and ask instead of guessing.

## Picking up work

- **Read before changing code.** [CONTRIBUTING](CONTRIBUTING.md) owns setup and the definition of done. [Testing](docs/engineering/TESTING.md) owns which checks cover which area. [Code quality standards](docs/engineering/CODE_QUALITY_STANDARDS.md) own how a change is built and proven. [Status](docs/release/STATUS.md) owns what is shipped versus pending. [Roadmap](ROADMAP.md) owns order and acceptance gates. Link to these; do not copy their commands here.
- **Choose the gate that owns the surface.** Python gameplay changes use the deterministic suite, the coverage floor, and seeded gameplay QA in [Testing](docs/engineering/TESTING.md#required-local-checks). Native rules and persistence use native qualification in [Testing](docs/engineering/TESTING.md#test-layers), and a save change includes the compatibility decision required by [CONTRIBUTING](CONTRIBUTING.md#definition-of-done). Godot, export presets, and packaging use the current-platform packaged-player smoke in [Testing](docs/engineering/TESTING.md#required-local-checks). Hosted three-platform qualification stays the separate obligation recorded in [Status](docs/release/STATUS.md). Release and evidence changes follow [Status](docs/release/STATUS.md) and the [roadmap quality ladder](ROADMAP.md#quality-ladder). Automatic qualification stays separate from the open playtest evidence those documents list, including the agentic findings tracked in [#51](https://github.com/blisspixel/VibeSnake/issues/51). The shipped native player is qualified by the native and packaged-player gates.
- **Only take an issue labelled `ready`.** Ready means it states a goal, scope, what it must not touch, and "Done when" checks that can fail. If an issue lacks them, add a dated comment proposing them and stop. Scoped work uses the [work item form](.github/ISSUE_TEMPLATE/work-item.yml). A playtest observation or other note uses the [playtest note form](.github/ISSUE_TEMPLATE/playtest-note.yml).
- **Done means the named checks ran and passed,** plus `CI complete` green on `main` at that revision. Do not report done from reasoning alone, and never weaken a checker, fixture, or the coverage floor to go green.
- **Stop after the same failure twice.** Leave a dated note on the issue saying what failed and what was tried, then move on or ask.
- **Ask before:** a new dependency, a save, replay, ruleset, or protocol compatibility change, publishing or signing, or anything irreversible. Label the issue `needs-decision` and say what the decision is.
- **Record progress on the issue,** not in a chat. Close the issue in the same change that meets its checks.
- **When an item slips past its target date, note the cause in the issue,** since it usually points at the next limiting factor to fix. Depth over speed still wins for design work.
