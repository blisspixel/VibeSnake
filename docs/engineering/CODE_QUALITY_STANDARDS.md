# Code Quality Standards

## Purpose

This document is the engineering contract for Vibe Snake. It applies to Python reference code, the pure C# rules kernel, the Godot application shell, content tools, tests, build scripts, and release automation. A change is acceptable only when it improves the intended product without making correctness, diagnosis, portability, security, or future change harder.

The roadmap defines what to build. Architecture documents define ownership. This document defines how every change is built and proven.

## Non-negotiable standard

Production work must be simple, correct, secure, performant, readable, maintainable, and evidence-backed.

- Keep one authoritative implementation of meaningful domain logic. Tiny local duplication is acceptable when an abstraction would be less clear.
- Do not leave `TODO`, `FIXME`, placeholder assertions, empty implementations, commented-out code, unreachable branches, or obsolete compatibility shims.
- Use names that express the game contract. Avoid generic names such as `data`, `manager`, or `helper` when a narrower domain name is available.
- Make invalid states difficult to construct and reject invalid external data at the boundary.
- Prefer explicit data flow, deterministic state, typed events, and small pure functions over hidden mutation.
- Preserve player data, input safety, and optional-content isolation on every failure path.
- Keep canonical documentation truthful. Passing tests do not justify a broader support or quality claim than the tests actually prove.
- Canonical source and documentation contain no emoji or em dash.

## Change design

Before editing a shared, public, or widely imported boundary:

1. Identify its callers, state owners, serialization contracts, fixtures, and likely tests.
2. Write the player-visible or tool-visible behavior and failure contract.
3. Choose the smallest coherent boundary that solves the whole stated problem.
4. Add a failing test for the defect or new behavior when the boundary is automatable.
5. Implement without speculative extension points.
6. Remove superseded paths in the same change.
7. Run focused checks, then the complete applicable quality gate.

Do not build a second framework inside the project. A new abstraction must remove real duplication, isolate a real dependency, protect an invariant, or make an acceptance gate executable.

## Architecture boundaries

### Deterministic rules

`VibeSnake.Rules` owns all scored gameplay state and resolution. It may depend on the .NET base class library and deliberately reviewed packages only. It must not reference Godot, Pygame, rendering, audio, files, clocks, environment variables, platform APIs, or global random state.

One rules step accepts explicit state and logical commands, then produces the next canonical state and ordered typed events. Rules code must:

- Use integer simulation steps and project-owned value types.
- Use named, versioned, serializable random streams.
- Preserve event order as part of the public contract.
- Include every behavior-affecting field in canonical restoration and hashing.
- Reject unknown, missing, inconsistent, non-canonical, or future incompatible state without partial mutation.
- Keep presentation-only data out of state hashes and replay outcomes.

### Godot shell

Godot owns scenes, input devices, windows, rendering, effects, UI, localization, audio devices, buses, resources, and platform lifecycle. Godot code submits logical commands and consumes snapshots and events. It does not mutate rules state directly.

Scenes and nodes must release owned resources explicitly, survive optional device and content failure, and produce a warning-free, leak-free clean exit. Presentation timing may interpolate but may never change rules cadence or outcomes.

### Python reference

Python remains a migration oracle until the 0.3 parity and artifact gates pass. Change it only to repair a verified defect, express a shared contract, improve reference evidence, or unblock migration. New Python domain behavior needs explicit typing at public boundaries, deterministic dependency injection, focused exceptions, and tests through the real integration boundary.

Do not duplicate broad new gameplay features in Python and C#. Record intentional behavior corrections in `PARITY_DECISIONS.md` and regenerate affected fixtures deterministically.

### Persistence and content

Files, saves, replays, imported personalities, manifests, and content packs are untrusted input. Their boundary must:

- Constrain byte size, nesting, counts, identifiers, numeric ranges, and path length before expensive work.
- Reject absolute paths, traversal, alternate separators, case-insensitive collisions, symbolic links, and entries outside the approved root.
- Reject unknown or duplicate JSON fields when the schema is strict.
- Validate schema and compatibility before interpreting content.
- Keep the original input intact on failure and return an actionable error without exposing machine paths.
- Write player data atomically and never overwrite a future schema.
- Never execute code from a 1.0 content pack.

## Language standards

### C# and .NET

- Keep nullable reference types enabled and resolve every warning.
- Pin the stable SDK analyzer wave explicitly. The August 2026 baseline is `.NET 10.0-recommended`; newer stable waves require a dedicated qualification change rather than silent drift.
- Treat compiler and analyzer warnings as errors in normal and CI builds.
- Enforce root EditorConfig formatting and selected code style at build time, including braces, final newlines, whitespace, and formatter diagnostics.
- Prefer immutable records and readonly value types for state and events.
- Use precise exception types and validate arguments at public boundaries.
- Avoid reflection, culture-sensitive serialization, platform-default encodings, and unspecified iteration order in deterministic code.
- Dispose owned resources deterministically. Do not rely on finalizers for routine cleanup.
- Keep package restore locked and dependency versions explicit.

The repository pins its SDK and analyzer behavior. Analyzer suppressions require a narrow scope, a written reason, and a test or invariant that protects the suppressed behavior. A future SDK or analyzer wave is reviewed against current Microsoft and Godot guidance, applied in one isolated change, and qualified across the complete matrix before it becomes the new baseline.

### Python

- Ruff is mandatory. New or materially changed modules must satisfy the configured checks without per-file blanket suppression.
- Use `pathlib`, context managers, explicit encodings, and argument-list subprocess calls.
- Never use `eval`, `exec`, untrusted pickle data, shell interpolation, bare `except`, or silent exception swallowing.
- Use `secrets` for security-sensitive randomness and injected versioned streams for gameplay randomness.
- Keep imports free of device, network, and writable filesystem side effects where practical.
- Tests that need SDL, saves, files, or environment variables use isolated fixtures and temporary directories.

### PowerShell and workflows

- Set terminating error behavior for qualification scripts and check every external process exit code.
- Resolve and validate destructive or recursive targets before mutation.
- Use literal paths for filesystem operations and quote paths that may contain spaces or non-ASCII text.
- Verify downloaded tools before extraction or execution using a pinned cryptographic digest.
- Pin third-party GitHub Actions to a full immutable commit SHA and retain the release tag in a comment for reviewability.
- Grant the workflow and each job only the permissions it needs.
- A credential-only publisher without a checkout must pass the explicit repository to every repository-scoped CLI command or API path.
- A floating-release publisher must reconcile and verify its tag separately from its release object, tolerate a bounded number of transient API failures, and be safe to rerun after partial success.
- Never print secrets, credential fragments, authorization headers, or signed URLs.

## Testing standard

Tests are executable contracts, not coverage decoration.

Every changed behavior needs evidence for:

- Normal operation.
- Boundaries and equivalence classes.
- Invalid input and dependency failure.
- Reset, restart, teardown, or resource release.
- Compatibility and migration where data persists.
- Cross-platform behavior when a platform boundary is involved.

Tests must be deterministic, isolated, order-independent, and able to run repeatedly. A test may not depend on a real player profile, wall-clock race, ambient random state, network service, or source checkout path unless the test is explicitly an external or artifact gate.

Coverage requirements:

- Project line coverage never falls below 80 percent.
- New deterministic and persistence boundaries receive branch-oriented tests.
- The deterministic engine and persistence boundaries reach at least 90 percent branch coverage before the 0.4 acceptance gate.
- Coverage exclusions require a concrete reason and may not hide difficult production modules.
- Mutation tests target scoring, death resolution, rules identity, replay verification, and migrations before 1.0.

A flaky test is a defect. Do not quarantine it indefinitely, retry it until green, weaken the assertion, or replace it with a sleep. Find and control the nondeterministic dependency.

## Differential and replay proof

Cross-language parity compares normalized state and ordered events after every step. A parity claim must identify the ruleset, randomness policy, compared fields, excluded fields, seed or fixture, and number of executed steps.

Every mismatch must produce a retained reproducer containing the first divergent step, shortest proven command sequence, relevant expected and actual state, ordered events, environment identity, and one-command reproduction. A confirmed defect becomes a permanent regression fixture.

Replay verification must authenticate its canonical payload, validate compatibility before execution, detect the first divergence, preserve incompatible files, and never treat an integrity hash as publisher authenticity.

## Security and supply chain

- Keep real secrets outside source, fixtures, logs, manifests, artifacts, screenshots, and `.agent/` files.
- Use least-privilege workflow permissions and isolated release credentials.
- Default every workflow to read-only repository contents. Grant write permissions only on the job that performs attestation or publication.
- Pin every external GitHub Action to a full commit SHA and retain its reviewed release tag in a comment. The quality suite checks both `.yml` and `.yaml` workflows.
- Audit Python and NuGet dependencies in CI and before release. A known exploitable vulnerability blocks release unless an explicit, evidence-backed exception is recorded.
- Retain dependency locks, artifact SHA-256 manifests, an SBOM or equivalent inventory, and build provenance.
- Build release artifacts from a clean version-controlled revision on an isolated hosted runner.
- Verify provenance and artifact digests before promotion.
- Separate signing and notarization from ordinary build logic. Never store signing material in the repository.

Content hashes prove reviewed-byte integrity. Platform signatures and build attestations prove origin. Neither replaces schema validation, runtime isolation, or player-facing recovery.

The committed Dependabot policy covers pip, both NuGet roots, and GitHub Actions. Routine version-update pull requests remain disabled while the repository keeps one public `main` branch; vulnerability audits in CI remain release-blocking, and dependency or action updates are reviewed as isolated qualification changes. Packaging workflows install build tools from the hash-locked CI graph rather than upgrading from the network.

The floating `player-latest` source release runs only for the exact `main` revision that completed CI successfully. Manual dispatch may build the package for diagnosis, but it does not publish or replace the floating release.

## Performance and resource quality

Correct rules may never depend on renderer performance. Measure fixed-step throughput separately from frame time.

- Use p50, p95, and p99 frame times on named hardware for presentation claims.
- Bound command queues, imported collections, particles, audio voices, replay size, content scans, and retry loops.
- Avoid allocation in hot fixed-step paths unless measurement shows it is harmless.
- Test repeated run, death, restart, focus, device, scene, and quit cycles for monotonic memory, handle, node, or audio growth.
- Treat startup, save latency, content scan, and shutdown as product behavior with explicit budgets before release.

Do not optimize from intuition alone. Keep a simpler implementation until a representative benchmark proves a meaningful problem.

## Documentation and evidence

Canonical docs describe only verified behavior. Update status, roadmap evidence, changelog, testing instructions, and player-facing contracts in the same change that alters them.

Evidence must include the exact command, tool and rules versions, platform, input corpus, result, and retained artifact when relevant. A narrow unit test cannot prove an exported artifact, physical device, audible mix, visible readability, fun, or platform support.

Automatic QA owns correctness, determinism, compatibility, stress, and reproducible balance outliers. Human review owns comprehension, control feel, tension, agency, delight, fatigue, aesthetics, audio judgment, and desire to replay.

## Required gates

The canonical commands are maintained in [TESTING.md](TESTING.md) and [DEVELOPMENT.md](../guides/DEVELOPMENT.md). At minimum, applicable changes must pass:

1. Dependency-lock freshness, full-tree Ruff, executable source policy, and documentation validation.
2. Source content and pack validation.
3. Shared fixture regeneration checks.
4. Python tests with the 80 percent line floor.
5. Locked C# restore, transitive NuGet audit, warning-free build, formatting, analyzers, tests, and coverage.
6. Real Godot import and deterministic scene smoke.
7. Exported-player smoke and artifact inspection for artifact-affecting changes.
8. Resolved Python-lock and NuGet vulnerability audits.

CI must run the same gates. Local success is necessary but does not replace hosted platform evidence.

## Review checklist

Before handoff, answer yes with evidence:

- Does the change implement the complete stated contract without unrelated scope?
- Is meaningful domain logic owned once?
- Are state, event order, random use, failure behavior, and teardown explicit?
- Do tests fail for the original defect and pass for the right reason?
- Are line coverage and relevant branch coverage preserved or improved?
- Are external inputs bounded, validated, and isolated?
- Are secrets, source paths, development files, and unapproved content absent?
- Do formatting, analyzers, lint, docs, tests, smoke, and applicable artifact gates pass?
- Are comments and docs accurate about remaining uncertainty?
- Is there any dead, commented-out, placeholder, or silently ignored code left?

## Research basis

Standards review date: 2026-08-13. The repository follows stable generally available guidance and tracks future stable revisions through dedicated qualification changes. It does not enable preview analyzer or language waves in release work.

- [.NET code analysis](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview) documents SDK analyzers, recommended analysis modes, build-time style enforcement, and warning escalation.
- [.NET unit-testing practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices) emphasize fast, isolated, readable tests that protect design and regression behavior.
- [Godot command-line operation](https://docs.godotengine.org/en/4.7/tutorials/editor/command_line_tutorial.html) defines headless project execution, import, and automated export capabilities used by the native gate.
- [GitHub Actions secure use](https://docs.github.com/en/actions/reference/security/secure-use) recommends least privilege, safe secret handling, and pinning third-party actions to full commit SHAs.
- [SLSA Build Track 1.2](https://slsa.dev/spec/v1.2/build-track-basics) distinguishes artifact provenance, authenticated build origin, and isolated build-platform guarantees.
