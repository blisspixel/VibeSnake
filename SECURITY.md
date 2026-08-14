# Security Policy

## Supported versions

Vibe Snake is currently an alpha. Security fixes are applied to the latest code
on the default branch. No released version has a long-term support commitment yet.

## Report a vulnerability

Private vulnerability reporting is enabled. Submit sensitive reports through the
repository's [private vulnerability form](https://github.com/blisspixel/VibeSnake/security/advisories/new).
Never include exploit details, private data, or unpatched vulnerabilities in a
public issue.

Include the affected revision, platform, reproduction steps, impact, and any safe
supporting evidence. Expect an acknowledgement after a maintainer has reviewed the
report. Public disclosure is coordinated only after a fix or documented mitigation
is available.

## Review scope

Security review covers source code, native and Python dependency boundaries,
content-pack parsing, save and replay parsing, build and release automation, and
official player artifacts. The post-1.0 Agent Arena preview adds local MCP input,
public observation projection, bounded session ownership, verified replay save,
portable plugin manifests, and same-user named-pipe authentication to that scope.
It does not cover modified third-party builds or
services outside project control.

The official preview opens no network listener, accepts no arbitrary filesystem
path, rules configuration, executable, prompt, credential, or agent-authored code,
and never loads third-party executable plugins into Godot. MCP clients and external
agent processes are untrusted callers outside the trusted game process. Their own
services and behavior are outside project scope, but host flaws are in scope when
malicious inputs can expose human or private state, escape bounded storage, execute
supplied code, bypass bearer or viewer capabilities, or mutate rules or human
progression. A one-time viewer token is a local capability and is not a defense
against software already running as the same compromised user.

Optional agent public intent is a closed enum. It changes idempotent request
identity but has no rules, scoring, reward, replay, qualification, filesystem,
network, or execution authority.

Agent Passport display names are untrusted bounded presentation text. Avatar,
accent, and station identifiers must resolve through the closed public catalogs
before a session is created. Unknown identifiers and legacy or mixed passport
schemas fail closed, and none of these presentation fields may read or mutate
human progression, cosmetics, scores, or saves.

Signal School lesson definitions, progress, deltas, outcomes, and retries are
closed versioned contracts. Replay-trace requirements are independently checked
against the verified replay. Only `first-turn` accepts separate action-attempt
evidence, capped at 32 first-seen opposite reversals whose tick and state-hash
anchors were valid before the zero-step rejection. The witness stores a SHA-256
idempotency-key hash, never the raw key, prompt, intent, credential, or provider
data. Exact retries, conflicts, stale anchors, wrong profiles, and witnesses after
the cap do not add evidence. A successful outcome binds both replay and attempt
evidence hashes. Replay failure remains failed closed, returns no lesson outcome,
and offers only a fresh `start_lesson` descriptor for the same lesson and action
profile. Lesson completion is factual practice evidence, not identity, mastery,
or qualification.

Live Style Contract progress is reconstructed only from typed rules-advanced steps. Only successful finalization independently reconstructs the same facts from a verified replay and produces a replay-bound outcome.
The terminal style outcome is returned only after independent replay evaluation
matches the bounded live facts and carries the verified replay payload hash.
Malformed criterion shapes, arithmetic, catalog identity, evaluator output, or
replay divergence fail closed without a verified outcome. Declared intent,
viewer timing, and passport identity never contribute to style evidence.

Step and burst mutations share one idempotency-key namespace capped at 4,096
unique records per match. Known keys are never evicted; after the cap, every
unseen key fails closed without advancing rules. The burst
profile accepts one initial closed action and a maximum of 16 steps, then uses a
fixed public-event stop policy. It accepts no caller-defined actions, predicates,
callbacks, paths, rewards, or code. At capacity, the host may reclaim a live
handle only after 30 minutes without a valid handle-bearing host operation. The
opaque handle is the bearer capability; stdio has no separate client-authentication
layer. Reclamation
creates no result or replay, and viewer activity is never match control.

The repository must never contain real credentials, signing material, private
reports, or player data. See the
[code quality standard](docs/engineering/CODE_QUALITY_STANDARDS.md) for enforced
engineering controls.
