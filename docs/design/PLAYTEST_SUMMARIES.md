# Local playtest summary contract

Status: V070-05 complete automated foundation with the V070-09 schema 2 power-decision extension (2026-08-08).

Vibe Snake can retain a small, versioned set of balance facts from completed local human runs. Collection is off by default. A player must enable `Playtest summaries` in Gameplay settings before a run ends. The data remains under Godot `user://`, has no upload path, and can be exported or permanently deleted from Data settings.

This is a balance-review record, not analytics, an identity profile, or an input recording. It never contains a player name, account identifier, raw input event or timing, controller or keyboard identity, hardware details, operating-system details, system path, IP address, free-form text, replay commands, or crash diagnostics.

## Collection boundary

A record is eligible only when all of these conditions hold:

1. The local preference `localPlaytestSummariesEnabled` is true when the run ends.
2. The run is a terminal, seeded, normal human run.
3. The rules, mode, score category, configuration, and final state identities validate.
4. The summary can be serialized within the closed schema and storage limits below.

Turning collection off prevents future capture but does not silently delete existing summaries. Existing data remains visible by count in Data settings until the player uses the separate, confirmed delete action.

## Stored document fields

The local file is `user://playtest-summaries/summaries.json`. Its top-level object has exactly these fields. Unknown, duplicate, missing, invalid, and future-schema fields fail closed without overwriting the source.

| Field | Type | Contract |
| --- | --- | --- |
| `schemaVersion` | integer | Summary document schema, currently `2`. Schema 1 migrates after its original identity is verified. |
| `kind` | string | Exact value `vibesnake-local-playtest-summaries-v2`. |
| `collectionBasis` | string | Exact value `explicit-local-opt-in`. |
| `retentionLimit` | integer | Exact value `200`. |
| `summaries` | array | Oldest-to-newest validated summary objects. |

Each summary object has exactly these 26 fields:

| Field | Type | Balance-review purpose |
| --- | --- | --- |
| `summaryId` | string | Lowercase SHA-256 of the other exact summary facts. It detects duplication and alteration; it is not a player ID. |
| `capturedAtUtc` | string | Terminal capture time in canonical UTC milliseconds, used to order local observations. |
| `appVersion` | string | Application build version that produced the run. |
| `runKind` | string | Exact value `normal-human`; automated, tutorial, replay, and spectator runs are excluded. |
| `rulesetId` | string | Stable ruleset identity, currently `vibesnake-core`. |
| `rulesVersion` | integer | Exact rules behavior version used by the run. |
| `modeId` | string | Stable product mode identity, currently `classic` or `vibe`. |
| `modeVersion` | integer | Version of the selected mode contract. |
| `scoreCategoryId` | string | Fair-score category, including the Vibe adaptation boundary. |
| `configHash` | string | Lowercase SHA-256 of the complete effective run configuration. |
| `adaptationEnabled` | boolean | Whether the disclosed Vibe adaptive policy was active. Classic must be false. |
| `adaptivePolicyId` | string | Exact enabled or disabled adaptive-policy identity. |
| `adaptiveFinalState` | string | Terminal disclosed state: `disabled`, `support`, `standard`, or `pressure`. |
| `seed` | string | Unsigned decimal master seed needed to reproduce the rules run without losing integer precision in JSON tools. |
| `outcome` | string | Terminal result: `dead` or `won`. |
| `deathCause` | string | `none`, `self-collision`, or `starvation`; it must agree with the outcome. |
| `survivalSteps` | integer | Number of deterministic rules steps completed. |
| `score` | integer | Final score in the declared score category. |
| `finalLength` | integer | Snake length in the terminal state. |
| `foodEaten` | integer | Food collected during the run. |
| `wraps` | integer | Board-edge wraps completed during the run. |
| `nearMisses` | integer | Qualified near-miss events during the run. |
| `powerupsCollected` | integer | Power pickups collected during the run. |
| `comboPeak` | integer | Highest combo reached during the run. |
| `finalStateHash` | string | Lowercase deterministic final-state hash used to detect rules drift. |
| `powerDecisions` | array | Exactly nine catalog-ordered, aggregate-only lifecycle rows defined below. |

Each `powerDecisions` row has exactly these nine fields:

| Field | Type | Decision-review purpose |
| --- | --- | --- |
| `powerId` | string | Stable power ID: `shield`, `phase-shift`, `last-stand`, `slow-mo`, `boost`, `magnet`, `bait`, `gluttony`, or `segment-detach`. |
| `offered` | integer | Typed product offers shown during the run. |
| `detoursObserved` | integer | Offers approached after a direction change, counted at most once per offer. This is an observed route change, not inferred intent. |
| `collected` | integer | Pickups collected. |
| `activated` | integer | Effects activated. |
| `expired` | integer | Visible offers or active effects that expired. |
| `consumed` | integer | Held or active resources consumed by their rule. |
| `saved` | integer | Typed collision-prevention events attributed to the power. |
| `deathAdjacent` | integer | Terminal deaths within 20 rules ticks of the last related power event. At most one per row and run. |

Every count is nonnegative and relationship-checked. The rows retain no route coordinates, commands, raw inputs, input times, or device facts. Schema 1 records did not contain lifecycle evidence, so migration verifies their original `summaryId`, adds nine zeroed rows, and computes a new schema 2 identity without inventing observations.

## Export fields

`Export summaries` writes a local JSON file under `user://playtest-summaries/exports/`. It does not open a network connection or choose a path outside the application-owned user-data root. An export has exactly this envelope plus the same closed summary objects:

| Field | Type | Contract |
| --- | --- | --- |
| `schemaVersion` | integer | Summary schema version, currently `2`. |
| `kind` | string | Exact value `vibesnake-local-playtest-summary-export-v1`. |
| `exportedAtUtc` | string | Explicit export time in canonical UTC milliseconds. |
| `sourceDocumentSha256` | string | Lowercase SHA-256 of the canonical source document. |
| `summaryCount` | integer | Number of summary objects in this export. |
| `summaries` | array | Exact validated local facts present at export time. |

The game displays only a portable `user://` location. A support or research workflow may ask a player to share a chosen export later, but this release contains no uploader, network endpoint, background transmission, account, or automatic consent flow.

## Retention and deletion rules

1. Collection defaults off, including every migration from preferences schemas 1 through 6.
2. The source retains at most the newest 200 unique summaries. Appending an identical `summaryId` is idempotent. Oldest records are evicted first.
3. The canonical source document is limited to 512 KiB. An oversized file is rejected before its contents are read or parsed.
4. At most the newest 20 explicit exports are retained. Oldest application-owned exports are removed first.
5. Disabling collection retains existing local data and creates no new summaries.
6. Corrupt, unknown, conflicting, or future data is never replaced by an append or export attempt.
7. `Delete summaries` requires a separate confirmation that states deletion is permanent, has no backup, and has no remote copy. Confirmation deletes the source, all application-owned exports, and any owned interrupted-write temporary files.
8. Playtest-summary deletion is independent of preference, progression, personal-best, replay, optional-content, and recovery-backup reset categories.

## Evidence and interpretation boundary

Pure persistence tests lock exact fields, schema 1 identity-verified migration, power-count relationships, capture qualification, strict parsing, identity checks, idempotence, byte and count limits, export retention, corruption preservation, and deletion. Godot `local-playtest-summary-qualification-v1` evidence uses real keyboard and controller settings routes to prove default-off consent, preference round-trip, capture, export, cancellation, confirmed deletion, field allowlists, forbidden field families, and absence of upload code. `power-decision-qualification-v1` separately proves the complete eight-stage aggregate lifecycle and 20-tick death-adjacency boundary.

These facts do not establish human target ranges on their own. The observed AI distributions remain separate in [BALANCE_BASELINES.md](BALANCE_BASELINES.md). Any human target requires the reviewed structured-playtest stages in V070-06 and V070-07, with observation kept separate from interpretation.
