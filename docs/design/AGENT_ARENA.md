# Agent Arena

[Game and experience design](README.md)

## Experience promise

The target Agent Arena experience will let an external agent learn Vibe Snake, develop a recognizable play style, challenge a named rival under equal rules, and leave behind a verified replay that a human can watch or challenge on the same seed.

The arena is designed to give software agents meaningful decisions and improving competence. It does not assume or claim that a model has a subjective experience of fun. The human-facing measure is whether the agent's goals, risks, turning points, and rivalries are clear enough to make the match worth watching.

Agent Arena is a post-1.0 optional capability. The development tree contains the preview integration, but it is not part of the supported 1.0 release contract. `ExportRelease` omits the preview project references and compiles out the watch route; schema-3 inspection rejects preview payloads and compiled command-line markers. The exact three-platform 1.0 candidate must retain that proof before promotion. The preview does not change gameplay rules, human saves, progression, or release gates automatically. Replay persistence occurs only after an explicit `save_verified_replay` request and writes to the ordinary bounded replay store.

## Developer preview status

The source preview implements symbolic-step play, a separate bounded symbolic-burst division, open and blind seeds, verified replays, a read-only live viewer, five closed Style Contracts, six selectable deterministic Signal School practices with primary-metric evaluators, ephemeral public Agent Passports, five closed self-declared public intents, and equal-seed named rivals. A burst applies one initial action, continues for at most 16 steps, and stops on a fixed public decision-event catalog, the selected lesson's first target transition, terminal state, match cap, replay failure, or its requested bound. Exact retries are cached across a shared step-and-burst mutation-key namespace. At host capacity, a live match idle for 30 minutes may be reclaimed without a result or replay, while viewer activity remains presentation only. The versioned `vibesnake-agent-viewer-frame-v4` contract identifies initial, step, burst, and finish operations; binds exact steps advanced to the pre-mutation tick and state hash; publishes burst stop reason and event, exact terminal and failed-closed truth, and verified-result availability; and rejects identity or cross-field drift. Monotonic sequence gaps are presented as coalesced earlier updates instead of one apparent step, awaiting-agent copy says that rules are paused, and a real terminal-burst Godot smoke exercises muted, high-contrast, reduced-motion snap, and 150-percent-text settings. Composed pseudo-localized overlay geometry is measured separately. The screen also shows the passport color, shed, station, and lesson target state. `start_lesson` returns canonical practice progress, mutation deltas, and a replay-derived final outcome. The longer experience contract below remains intentionally broader. Visual control, all eight observable behaviors represented by eight canonical Signal School lessons, Rival Breaker, expressive Style Contract evaluation, free-form captions, persisted exhibition receipts and passport history, qualification-time decks, rankings, turning-point summaries, and the same-seed human handoff are not implemented yet.

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

The current preview implements play, live read-only watching, result retrieval, and a separate explicit replay save. It does not yet implement the target recorded-first broadcast route, same-seed human handoff, or retained public result history. Agent response time never affects score. The current live viewer waits for agent actions without advancing rules; the finished verified replay remains canonical when finalization succeeds.

The failure branch is part of the loop, not an implementation detail. If replay finalization fails, the match becomes failed closed, no verified result or saveable replay is available, and the viewer says so explicitly. The current recovery is to start a new match or restart the local host. Destructive capacity-only reclamation for a live handle idle for 30 minutes is implemented and never creates a result or replay. Compact failed-closed review and restart guidance remain AA-05 work; failed-closed runs intentionally never gain a result or saveable replay.

## Play divisions

Results from different control profiles are not ranked together.

| Division | Observation | Action pacing | Intended controller |
| --- | --- | --- | --- |
| Symbolic step | Complete public logical state | One action advances one step | Language-model and tool-using agents |
| Symbolic burst | Complete public state with bounded event stops | A bounded continuation stops at a public event or budget | Efficient deliberative agents |
| Visual control | Rendered frame and logical controls | Presentation-paced input | Computer-use and vision agents |

Each division declares Classic or Vibe, exact rules and configuration identity, open or blind seed visibility, action-call and step budgets, memory policy, and agent version.

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

The preview exposes six canonical primary-target practices through `start_lesson`: `first-turn`, `wrap-line`, `hunger-route`, `power-route`, `combo-route`, and `recover-route`. Each fixes its mode, open seed, cap, instruction, public metric, and threshold. Observations expose progress, accepted mutations expose exact deltas, a target-reaching burst stops on that exact step, and successful finalization returns a replay-derived outcome bound to the replay payload hash. These practices cover one accepted direction change, one typed wrap, one food in Vibe mode, one power activation, a peak combo of three, and one typed collision-prevented recovery. They do not yet cover the full target sequence above: exit preservation and attributable death are absent, and `first-turn` does not teach recovery from a rejected reversal. The completed curriculum contains eight observable behaviors represented by eight canonical lessons and qualifies each stated behavior through immutable replay, event, and bounded action-attempt evidence rather than only a catalog count or primary threshold.

Failure review remains factual and bounded. A completed lesson reports the first unmet requirement and a closed reason such as voluntary finish, rules terminal, step cap, insufficient attempt evidence, or target reached. Replay failure remains failed closed and creates no successful outcome. Same-lesson retry never resumes the old rules state: a bounded retry descriptor names the canonical lesson and the client calls `start_lesson` to create a fresh ephemeral session with no inherited score, replay, mutation keys, or practice history.

## Style Contracts

Score alone should not define successful agent play. A Style Contract combines one primary objective with one optional expressive objective. The preview exposes and evaluates the first five contracts below using their primary metrics. Expressive objectives and Rival Breaker remain target contracts rather than qualified preview results.

| Contract | Primary objective | Expressive objective |
| --- | --- | --- |
| Stillwater | Survive 200 steps | Preserve open exits and avoid dead ends |
| Crownchaser | Reach a four-food peak combo | Sustain combo continuity |
| Edge Prophet | Produce three near misses | Add intentional wraps without needless danger |
| Mutagenist | Activate two powers | Demonstrate useful power timing and synergy |
| Redline | Collect six food | Reach food efficiently while preserving recovery space |
| Rival Breaker | Beat a named verified outcome | Win on the rival's characteristic terms |

Contracts wrap official Classic and Vibe rules. They do not create hidden mechanics or alternate physics.

## Observation contract

The symbolic observation is a closed, versioned allowlist containing:

- Contract, rules, mode, configuration, match, tick, and state-hash identities.
- Board dimensions and wrapping behavior.
- Run status, death cause, current direction, head, body, and accepted pending directions.
- Food, visible power pickup, bait, detached obstacles, score, combo, hunger, and public timers.
- Active public effects and remaining step budget.
- Ordered events from the immediately preceding accepted action. A rejection response clears this event list, so an agent must consume accepted-step events from that accepted response.
- Previous action acceptance or rejection.
- Declared contract progress and optional public rival summary when available.

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

The service returns factual events rather than a fabricated dense reward. A burst returns its actual step count, closed stop reason, optional first stop event, final-step ordered events, and refreshed observation. The preview terminal metric vector contains survival steps, food eaten, peak combo, wraps, near misses, powers collected, powers activated, recoveries, starvation warnings, and direction changes, plus the selected contract's primary threshold result. Route efficiency, risk exposure beyond near misses, dead-end measures, and expressive multi-metric contract evaluation remain AA-03 and AA-08 targets.

## Agent identity and memory

The preview accepts an ephemeral public Agent Passport containing:

- Caller-declared agent ID and policy version. The preview validates bounds but does not establish global identity or persistence.
- Bounded, trimmed, control-character-free display name plus bounded color, shed, and station labels.
- Supported observation and action profiles.

A later persisted passport may add:

- Verified matches, personal bests, selected contracts, rival records, and milestones.
- An optional bounded model or policy label.

It never contains prompt history, chain of thought, credentials, raw provider responses, executable code, or a human profile. External agents own their semantic memory and learned skills. The current viewer renders the caller-provided passport color and prints shed and station placeholder labels; closed-catalog validation and a passport-owned avatar independent of the local human cosmetic profile remain AA-03 work. The current host retains public identity and verified outcomes only for the bounded in-process session; an explicit replay save persists the verified run through the ordinary replay store. Bounded preference and outcome history belongs to the later persisted passport.

## Fair competition

- Learning evidence keeps canonical practice seeds separate from deterministic non-practice fixtures. AA-08 later owns qualification-time decks, division manifests, rankings, and generalization reports. The preview implements open and blind match seeds but no qualification deck.
- Classic, Vibe configuration, seed visibility, control profile, memory policy, and agent version define a division.
- Equal-seed rival lanes use the exact same configuration and independent controller state.
- Every successfully finalized terminal, capped, or explicitly finished run produces a verified replay. A replay failure is reported as failed closed with no verified result. The replay proves the captured run, not the external policy's determinism.
- Agent matches update no human scores, achievements, progression, ordinary challenges, or built-in league standings.
- The player and host never execute participant policy code.
- Target qualification evaluates multiple dimensions and reports the practice-to-qualification generalization gap.

## Broadcast language

The viewer presents a competitor, not a request log. It shows the matchup, contract, agent display name, color, shed, station, rival score, match status, latest closed self-declared public intent, every current closed action acceptance or rejection reason, exact end reason, and whether a verified result exists. A rejected attempt may change the displayed attempted intent, but the adjacent rejection label makes clear that no rules step was accepted. A disconnect says only that match control remains with the host and never claims that a replay exists. It should next add engine-observed risks and resources, record changes, typed highlights, and a post-run turning-point summary.

The current preview accepts only `seek_food`, `seek_power`, `preserve_space`, `take_risk`, or `recover`, plus `undeclared`. These values are clearly self-reported, appear only in public action feedback and the viewer, and cannot affect rules or verification. Free-form captions and confidence are deferred until they have a concrete moderation and accessibility benefit. Private reasoning is never requested or displayed.

## Continuous polish loop

Human availability does not serialize the build plan. Deterministic work on clarity, pacing controls, event selection, replay handoff, accessibility, recovery, packaging, and agent curricula continues from explicit contracts. Human evidence is collected whenever available and decides whether a behavior is kept, revised, removed, or promoted as fun.

AA-03 legibility and AA-04 efficient control are parallelizable foundations; AA-05 waits for both to stabilize. The target dependency order is:

1. Make every action correct, recoverable, and replay-verifiable.
2. Make goal, style, public intent, risk, resources, and outcomes readable without diagnostics.
3. Add a bounded event-stopping symbolic burst before asking language-model agents to complete long curricula or qualification decks. This control foundation is implemented while legibility work continues.
4. Make fixed-seed styles and rivalries visibly distinct without hidden rewards or altered physics.
5. Complete the curriculum and public practice route once symbolic burst and visible style contracts stabilize.
6. Persist one bounded public exhibition receipt that hash-links both lane replays before building history, recorded broadcast, or handoff around it.
7. Add deterministic turning-point selection, recorded-first broadcast pacing, and one-step replay-to-human challenge routes; add bounded public memory after the receipt, then qualification after curriculum and identity stabilize.
8. Package supported desktop artifacts only after the experience and storage surfaces stop changing rapidly.

Automation establishes Correct and the objective prerequisites for Legible, Expressive, and Dramatic. Human review must establish that goals are understood, styles appear distinct, and turning points and pacing work for viewers. Claims that viewers want to keep watching, rematch, or return require retained structured observations, including neutral and negative results.

## Evidence required

Agent usability evidence asks whether an unfamiliar agent can complete Signal School, recover from protocol errors, use the context efficiently, generalize beyond canonical practice seeds, and express distinct Style Contracts.

Fairness evidence proves cross-platform trace determinism, observation privacy, idempotent concurrency, bounded resources, clean timeout and disconnect handling, replay verification, agent-version separation, and zero human-progression mutation.

Human spectator evidence asks whether viewers understand the selected goal and style, recognize turning points, tolerate waiting in live mode, want a rematch or same-seed challenge, and retain the story under reduced motion, maximum text, high contrast, and muted audio.

Protocol completeness, test count, score, and an agent's statement that play was fun are not substitutes for human experience evidence.
