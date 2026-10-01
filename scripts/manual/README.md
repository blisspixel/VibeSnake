# Manual Native Tools

Manual operators now run through native `RepositoryChecks`. They require explicit inputs or interaction and stay outside combined `all` and ordinary CI. This directory retains their operator guidance; the superseded Python scripts have been removed.

Run commands from the repository root:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- radio-preview . list "C:/review/radio"
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- radio-preview . play "C:/review/radio" "C:/tools/ffplay.exe"
```

On macOS or Linux, supply the corresponding absolute review-directory and FFplay executable paths. Review inputs must be in the ignored `archive/` tree or outside the repository, never under public `assets/`. Custom directory links are rejected; verified macOS system aliases for `/var` and `/tmp` resolve to their `/private` paths.

`list` prints available non-empty samples from the fixed eight-station catalog without opening an audio device. `play` requires an explicit existing FFplay executable. Press Enter to start a sample and Enter again to stop and continue; `q` or end-of-input stops the sequence. Each player process has a 30-minute playback limit and bounded cleanup. Paths are revalidated before playback. Sample playback does not record or approve listening decisions.

| Operator | Purpose |
| --- | --- |
| `product-review-prepare` | Recompute a downloaded Release matrix and prepare pending candidate-bound session templates |
| `radio-audio` | Fully decode and measure radio sources without changing them |
| `radio-review` | Prepare reproducible station review copies in an ignored workspace |
| `radio-listening` | Rehash exact review copies, prepare a pending listening template, or validate an explicit human record |
| `radio-preview` | List or interactively play fixed cross-station samples |

These routes do not grant release approval, change curation or export eligibility, replace public source bytes, or publish a release. The [tool reference](../README.md) gives their full command shapes. See the [audio workflow](../../docs/content/AUDIO.md) and [migration map](../../docs/engineering/MIGRATION_MAP.md#repository-wide-python-retirement) for evidence boundaries and remaining Python ownership.
