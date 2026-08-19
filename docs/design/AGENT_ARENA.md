# Agent Arena

[Game and experience design](README.md)

## Experience promise

The target Agent Arena experience will let an external agent learn Vibe Snake, develop a recognizable play style, challenge a named rival under equal rules, and leave behind a verified replay that a human can watch or challenge on the same seed.

The arena is designed to give software agents meaningful decisions and improving competence. It does not assume or claim that a model has a subjective experience of fun. The human-facing measure is whether the agent's goals, risks, turning points, and rivalries are clear enough to make the match worth watching.

Agent Arena is a post-1.0 optional capability. The development tree contains the preview integration, but it is not part of the supported 1.0 release contract. `ExportRelease` omits the preview project references and compiles out the watch route; schema-3 inspection rejects preview payloads and compiled command-line markers. The exact three-platform 1.0 candidate must retain that proof before promotion. The preview does not change gameplay rules, human saves, progression, or release gates automatically. Replay persistence occurs only after an explicit `save_verified_replay` request and writes to the ordinary bounded replay store.

## Developer preview status

The source preview implements symbolic-step play, a separate bounded symbolic-burst division, open and blind seeds, verified replays, a read-only live viewer, five closed two-criterion Style Contracts, exactly eight deterministic Signal School practices with two ordered factual requirements each, ephemeral public Agent Passports with closed avatar, accent, and station catalogs, five closed self-declared public intents, and equal-seed named rivals. A burst applies one initial action, continues for at most 16 steps, and stops on a fixed public decision-event catalog, the selected lesson's first all-requirements transition, terminal state, match cap, replay failure, or its requested bound. Exact retries are cached across a shared step-and-burst mutation-key namespace and never duplicate lesson attempt evidence. At host capacity, a live match idle for 30 minutes may be reclaimed without a result or replay, while viewer activity remains presentation only.

The versioned `vibesnake-agent-viewer-frame-v9` contract identifies initial, step, burst, and finish operations; binds exact steps advanced to the pre-mutation tick and state hash; publishes burst stop reason and event, exact terminal and failed-closed truth, a verified combined-evidence lesson outcome or replay-verified style outcome, and verified-result availability; and rejects unknown-catalog, identity, lesson, style, survival, or cross-field drift. Its `vibesnake-agent-survival-state-v1` block reports the structural open exits out of three non-reversal candidates, the closed pressure tier that count crosses, and the four held recovery resources in a fixed order; the viewer recomputes every value from the public observation and rejects a frame that disagrees with its own board. The block names observed danger, never a direction. Monotonic sequence gaps are presented as coalesced earlier updates instead of one apparent step, awaiting-agent copy says that rules are paused, and a real terminal-burst Godot smoke exercises muted, high-contrast, reduced-motion snap, and 150-percent-text settings. Composed pseudo-localized overlay geometry is measured separately. The screen resolves the agent-owned avatar and accent independently of human cosmetics and shows the catalog station plus lesson requirement state. `start_lesson` returns canonical v2 definitions, v3 progress, v2 mutation deltas, and v3 outcomes when verification succeeds. Successful live progress recommends `finish_match` and a reached-target outcome omits retry guidance; incomplete or failed-closed practice alone offers a fresh-session retry descriptor. Style v3 publishes threshold-crossing fields rather than grade-like satisfaction fields. The longer experience contract below remains intentionally broader. A verified exhibition can now be kept: `archive_exhibition` writes one canonical receipt, plus the saved replay file name of every lane it contains, into a bounded 32-entry local archive outside the supported Persistence assembly, atomically, without overwriting a different exhibition and without repairing a document that fails to recompute its own hashes. The exhibition browser and the same-seed human handoff are implemented: a preview-only launch lists what the archive kept, watches a kept exhibition through ordinary verified replay playback, and starts the exact same seed as a human challenge in its own score category. Visual control, Rival Breaker, free-form captions, qualification-time decks, and rankings are not implemented yet. Turning-point summaries and recorded-first montage playback are implemented for archived exhibitions. The local passport store is implemented. Human review must still determine whether the factual composite style and curriculum presentation is legible and entertaining.

## Target core loop

```text
Choose identity, rival, mode, seed division, and Style Contract
  -> observe public state
  -> commit one bounded step or burst mutation
  -> receive factual event feedback
  -> complete, cap, or abort with a verified replay
  -> watch the broadcast or take the same-seed challenge
  -> retain a public result and choose a rematch or new style
```

The current preview implements play, live read-only watching, result retrieval, a separate explicit replay save, the exhibition browser with same-seed challenge, a bounded local public-identity store, and a recorded-first story playback for archived exhibitions. Confirm on the exhibition browser plays the montage rather than dumping the raw tape; skip windows jump, linger holds a turning point, a lane switch is a cut, first-crossing style and lesson highlights come from the named agent tape, and the overlay names the current highlight, lane, pace, and lead. Human spectator-appeal evidence remains open. Agent response time never affects score. The current live viewer waits for agent actions without advancing rules; the finished verified replay remains canonical when finalization succeeds.

The failure branch is part of the loop, not an implementation detail. If replay finalization fails, the match becomes failed closed, no verified result, lesson outcome, or saveable replay is available, and the viewer says so explicitly. Signal School returns a bounded descriptor that directs the caller to `start_lesson` with the same lesson and action profile in a fresh session. It never resumes old rules state or carries score, replay, mutation keys, or practice history forward. Destructive capacity-only reclamation for a live handle idle for 30 minutes is implemented and never creates a result or replay.

## Play divisions

Results from different control profiles are not ranked together.

| Division | Observation | Action pacing | Intended controller |
| --- | --- | --- | --- |
| Symbolic step | Complete public logical state | One action advances one step | Language-model and tool-using agents |
| Symbolic burst | Complete public state with bounded event stops | A bounded continuation stops at a public event or budget | Efficient deliberative agents |
| Visual control | Rendered frame and logical controls | Presentation-paced input | Computer-use and vision agents |

Every current division declares Classic or Vibe, exact rules and configuration identity, open or blind seed visibility, a step cap, bounded mutation capacity, memory policy, and agent version. Signal School publishes 16 exact lesson/profile action-call and UTF-8 measurements over the discovered MCP arguments objects and structured responses. Bounded straight-line burst fixtures choose an observation-derived bound from 1 through 16, use no more action calls than the paired step route, and reduce calls for at least six of eight lessons. Every observation change requires review. These are deterministic regression measurements, not token estimates, universal provider costs, or evidence of mastery.

## Signal School

Signal School is a deterministic curriculum, not a hidden tutorial score. Lessons should teach one observable contract at a time:

1. Make a legal turn and recover from a rejected reversal.
2. Use board wrapping intentionally.
3. Eat before starvation.
4. Preserve an exit as the body grows.
5. Collect and use a power.
6. Recover from danger with protection.
7. Complete a short combo route.
8. Identify an attributable death from returned events.

The preview exposes exactly eight ordered practices through `start_lesson`: `first-turn`, `wrap-line`, `hunger-route`, `exit-route`, `power-route`, `recover-route`, `combo-route`, and `death-read`. Each fixes its mode, open seed, cap, instruction, and exactly two ordered factual requirements. `first-turn` pairs one valid-state opposite-reversal rejection with a later replay `DirectionChanged` step. `wrap-line` pairs `Wrapped` with Running on the same step. `hunger-route` pairs `AteFood` with eating before starvation death. `exit-route` pairs food growth with a Running post-step state that has at least two structural non-reversing exits. `power-route` pairs collection with activation of the same power kind at or after collection in event order. `recover-route` pairs a typed non-none `CollisionPrevented` cause and known power with Running on the same step. `combo-route` requires at least three food and peak combo at least three. `death-read` pairs a terminal non-none death cause with a terminal `Died` event reporting the same cause.

Replay-trace requirements are verified independently at finalization. `first-turn` alone uses a separate maximum-32 attempt-witness chain for first-seen opposite reversals rejected at a valid tick and state hash. Exact retries, conflicts, stale anchors, wrong profiles, and a 33rd relevant rejection do not add evidence. A successful lesson outcome binds replay and attempt evidence hashes, reports the actual end reason, uses the closed review codes target reached, replay requirement unmet, or insufficient attempt evidence, and omits retry guidance when the target is reached. It reports ordered requirement state and the first unmet requirement. Live completion recommends `finish_match`. Replay failure remains failed closed and creates no lesson outcome. Incomplete and failed-closed practice alone receive a retry descriptor that starts a fresh canonical practice. `death-read` teaches a deterministic self-collision because starvation exceeds the practice cap. Completion is factual practice evidence, not mastery, intent, planning, personality, or qualification.

## Style Contracts

Score alone should not define successful agent play. The preview evaluates five closed Style Contracts through exactly two ordered facts reconstructed from rules-advanced steps. Live values are observations and may rise or fall. A successfully finalized result independently replays the action trace, requires exact agreement with the live fact accumulator, and binds the two-criterion outcome to the verified replay payload hash.

| Contract | Criterion one | Criterion two |
| --- | --- | --- |
| Stillwater | At least 200 rules-advanced steps | At least 9,900 basis points of all rules-advanced steps end Running with at least two structural non-reversing exits; terminal steps remain in the denominator |
| Crownchaser | Peak combo at least 4 | Uninterrupted current combo-chain food divided by all food through the first combo of 4 equals 10,000 basis points |
| Edge Prophet | At least 3 positive NearMiss events at the post-step head with at least three occupied non-wrapping adjacent body cells under pinned `vibesnake-core@4` | At least 1 of those events has Wrapped in the same rules-advanced step |
| Mutagenist | At least 2 distinct activated power kinds | At least 2 concurrently active power kinds in one post-step state |
| Redline | At least 6 food collected | At least 6,500 basis points of rules-advanced steps with visible pre-step food either eat or reduce wrapped distance to that exact target, end non-dead, and retain a structural exit unless won |
| Rival Breaker, planned for AA-08 | Beat a named verified outcome | Win on the rival's characteristic terms |

Structural exits use wrapped collision geometry, the departing-tail and food-growth rules, and no temporary collision immunity. Redline credits an eligible step when it eats the pre-step target, including when Magnet moved that target during the step, or reduces wrapped Manhattan distance to the exact captured target, then leaves a non-dead state with an onward structural exit unless the run is won. Rate criteria expose exact integer numerators and denominators and use floor basis points with zero for an empty denominator. Edge Prophet derives its rewarded body-proximity subtype from positive head-position near-miss evidence plus post-step body adjacency under `vibesnake-core@4`; a later rules identity must requalify that evaluator. These facts do not prove calmness, intent, planning, useful timing, mastery, personality, or spectator appeal. Contracts wrap official Classic and Vibe rules and create no hidden mechanics or alternate physics.

## Observation contract

The symbolic observation is a closed, versioned allowlist containing:

- Contract, rules, mode, configuration, match, tick, and state-hash identities.
- Board dimensions and wrapping behavior.
- Run status, death cause, current direction, head, body, and accepted pending directions.
- Food, visible power pickup, bait, detached obstacles, score, combo, hunger, and public timers.
- Active public effects and remaining step budget.
- Ordered events from the immediately preceding rules-advanced step. Preflight and other zero-step rejections clear this event list; a post-step `replay_failure` retains only that exceptional rules-advanced step's events.
- Previous action acceptance or rejection.
- Either Style Contract progress with exactly two ordered live criteria and exact rate numerators and denominators, or Signal School v2 progress with exactly two ordered requirements, evidence state, bounded attempt count and hash, and fresh-session retry guidance when applicable. An optional public rival summary is included when available.

It excludes random-generator state, future spawns, controller decisions, other live actions, private user data, local paths, diagnostics, credentials, prompts, hidden reasoning, and engine-computed route advice.

Blind-seed observations omit the seed until the final verified lane result. Open-seed observations expose it and form a separate legitimate simulation-friendly division.

## Action contract

The base action is `up`, `right`, `down`, `left`, or `continue`. Every mutating request supplies the expected tick, expected state hash, and an idempotency key. It may also declare `seek_food`, `seek_power`, `preserve_space`, `take_risk`, or `recover` as a public presentation-only intent.

- A valid `play_move` request advances exactly one rules step. A valid `play_burst` request advances the returned `steps_advanced` count from 1 through its requested bound.
- `continue` advances without queuing a direction.
- A stale request, illegal reversal, conflicting idempotency key, terminal request, or invalid payload advances no rules state.
- Retrying the same key with the same request returns the original response.
- A changed public intent changes the idempotent request identity but never changes rules, scoring, rewards, replay verification, or qualification.
- The `four-direction-burst-v1` profile advances at most 16 steps, applies only one initial turn, and stops through fixed `decision-event-stop-v1` public events rather than caller-defined predicates.

The service returns factual events rather than a fabricated dense reward. A burst returns its actual step count, closed stop reason, optional first stop event, final-step ordered events, and refreshed observation. The preview terminal metric vector contains survival steps, food eaten, peak combo, wraps, near misses, powers collected, powers activated, recoveries, starvation warnings, and direction changes. A styled result adds exactly two replay-derived criterion results bound to the same verified replay hash. Broader risk exposure, recovery-resource explanation, and qualification-time comparison remain AA-03 and AA-08 targets.

## Agent identity and memory

The preview accepts an ephemeral public Agent Passport v4 containing:

- Caller-declared agent ID and policy version. The preview validates bounds but does not establish global identity or persistence.
- Bounded, trimmed, control-character-free display name plus avatar, accent, and station IDs from closed public catalogs.
- The fixed `symbolic-step-v4` observation profile and one supported action profile.

The AA-07 persisted record adds:

- Verified exhibition counts, personal best score, selected contracts, rival ahead/level/behind records, and milestones that point at the earning receipt.
- An optional bounded policy-version history.

It never contains prompt history, chain of thought, credentials, raw provider responses, executable code, a display name, or a human profile. External agents own their semantic memory and learned skills. The current viewer resolves the passport avatar and accent independently of the local human cosmetic profile and renders the catalog station label. The station catalog establishes presentation identity only; broadcast audio and host content remain separately unapproved. The current host retains public identity and verified outcomes for the bounded in-process session; an explicit replay save persists the verified run through the ordinary replay store; and an explicit `record_passport` folds a verified receipt into a bounded local public-identity store that never stores a display name or human profile. Global identity authentication, qualification-time standings, and preference history remain later work.

## Fair competition

- Learning evidence keeps canonical practice seeds separate from deterministic non-practice fixtures. AA-08 later owns qualification-time decks, division manifests, rankings, and generalization reports. The preview implements open and blind match seeds but no qualification deck.
- Classic, Vibe configuration, seed visibility, control profile, memory policy, and agent version define a division.
- Equal-seed rival lanes use the exact same configuration and independent controller state.
- Every successfully finalized terminal, capped, or explicitly finished run produces a verified replay. A replay failure is reported as failed closed with no verified result. The replay proves the captured run, not the external policy's determinism.
- Agent matches update no human scores, achievements, progression, ordinary challenges, or built-in league standings.
- The player and host never execute participant policy code.
- Target qualification evaluates multiple dimensions and reports the practice-to-qualification generalization gap.

## Broadcast language

The viewer presents a competitor, not a request log. It shows the matchup, contract, agent display name, catalog-bound avatar, accent, and station, both factual style criteria or both ordered lesson requirements when selected, rival score, match status, latest closed self-declared public intent, every current closed action acceptance or rejection reason, exact end reason, and whether a verified result exists. Live copy says observed; a matching terminal style outcome says replay verified, while a lesson outcome says verified evidence because first-turn may bind a separate attempt witness. Failed-closed lesson copy reports unavailable verified evidence and offers a fresh practice instead of implying an outcome. A rejected attempt may change the displayed attempted intent. Preflight and logical rejection labels mean no rules step advanced; `replay_failure` with `rules_advanced=true` reports the exceptional real step and failed-closed evidence state. A disconnect says only that match control remains with the host and never claims that a replay exists. It should next add engine-observed risks and resources, record changes, typed highlights, and a post-run turning-point summary.

The current preview accepts only `seek_food`, `seek_power`, `preserve_space`, `take_risk`, or `recover`, plus `undeclared`. These values are clearly self-reported, appear only in public action feedback and the viewer, and cannot affect rules or verification. Free-form captions and confidence are deferred until they have a concrete moderation and accessibility benefit. Private reasoning is never requested or displayed.

## Continuous polish loop

Human availability does not serialize the build plan. Deterministic work on clarity, pacing controls, event selection, replay handoff, accessibility, recovery, packaging, and agent curricula continues from explicit contracts. Human evidence is collected whenever available and decides whether a behavior is kept, revised, removed, or promoted as fun.

AA-03a catalog identity and AA-03b replay-derived style truth now provide AA-05's stable machine dependency, while observed risk and resource presentation and human legibility evidence remain active AA-03 lanes. AA-04 efficient control is complete. The target dependency order is:

1. Make every action correct, recoverable, and replay-verifiable.
2. Make goal, style, public intent, risk, resources, and outcomes readable without diagnostics.
3. Add a bounded event-stopping symbolic burst before asking language-model agents to complete long curricula or qualification decks. This control foundation is implemented while legibility work continues.
4. Make fixed-seed styles and rivalries visibly distinct without hidden rewards or altered physics.
5. Retain the eight-lesson curriculum, public practice routes, non-practice evaluator fixtures, exact interaction measurements, and burst-efficiency regression. Add human legibility evidence before claiming the curriculum is learnable or fun.
6. Persist one bounded public exhibition receipt that hash-links both lane replays before building history, recorded broadcast, or handoff around it. Implemented: the archive keeps the receipt beside both lane replay file names, bounded and atomic, and is the substrate the browser and passport history will read.
7. Add deterministic turning-point selection, recorded-first broadcast pacing, and one-step replay-to-human challenge routes; add bounded public memory after the receipt, then qualification after curriculum and identity stabilize.
8. Package supported desktop artifacts only after the experience and storage surfaces stop changing rapidly.

Automation establishes Correct and the objective prerequisites for Legible, Expressive, and Dramatic. Human review must establish that goals are understood, styles appear distinct, and turning points and pacing work for viewers. Claims that viewers want to keep watching, rematch, or return require retained structured observations, including neutral and negative results.

## Evidence required

Agent usability evidence asks whether an unfamiliar agent can complete Signal School, recover from protocol errors, use the context efficiently, generalize beyond canonical practice seeds, and express distinct Style Contracts.

Fairness evidence proves cross-platform trace determinism, observation privacy, idempotent concurrency, bounded resources, clean timeout and disconnect handling, replay verification, agent-version separation, and zero human-progression mutation.

Human spectator evidence asks whether viewers understand the selected goal and style, recognize turning points, tolerate waiting in live mode, want a rematch or same-seed challenge, and retain the story under reduced motion, maximum text, high contrast, and muted audio.

Protocol completeness, test count, score, and an agent's statement that play was fun are not substitutes for human experience evidence.
