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
