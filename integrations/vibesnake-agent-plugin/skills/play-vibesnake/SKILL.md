---
name: play-vibesnake
description: Play deterministic Vibe Snake matches and canonical Signal School practice through the local MCP host, pursue a declared Style Contract, challenge a seed, and save a verified replay for a human to watch. Use when asked to play, practice, evaluate, or spectate Vibe Snake through the start_match, start_lesson, observe_match, play_move, play_burst, finish_match, get_match_result, get_exhibition_receipt, or save_verified_replay tools.
---

# Play Vibe Snake

Treat the MCP tool schemas and returned observations as authoritative. Use this skill for play strategy and the safe match workflow. Never infer hidden state or ask for private reasoning.

## Prepare

1. Read `vibesnake://agent/rules`, `vibesnake://agent/modes`, and `vibesnake://agent/identity` once per host version. Read `vibesnake://agent/styles`, `vibesnake://agent/rivals`, or `vibesnake://agent/signal-school` when using those experiences.
2. Choose `classic` for fixed rules or `vibe` for the declared adaptive policy.
3. Choose `open` to receive the seed during play or `blind` to receive it only in the result.
4. Choose one style for the run. Each style publishes exactly two closed, replay-derived criteria. Live values are observations, while only the terminal style outcome is replay verified:
   - Stillwater: survive at least 200 rules-advanced steps. Among accepted steps whose post-step state is Running, at least 99 percent must retain two structural non-reversing exits. Terminal post-step states are not in that rate denominator.
   - Crownchaser: reach a peak combo of at least 4 with a 100 percent uninterrupted food chain through the first combo of 4.
   - Edge Prophet: produce at least 3 positive NearMiss events at the post-step head with at least three occupied non-wrapping adjacent body cells, including at least 1 with Wrapped in the same rules-advanced step. This `vibesnake-core@4` reconstruction does not prove intent.
   - Mutagenist: activate at least 2 distinct power kinds and reach at least 2 concurrently active power kinds.
   - Redline: collect at least 6 food. On at least 65 percent of rules-advanced steps with visible pre-step food, eat or reduce wrapped distance to that exact captured target, end non-dead, and retain a structural exit unless won. Eating qualifies even if Magnet moves the food during the step.
   Rate criteria use floor integer basis points and return the exact numerator and denominator. An empty denominator produces zero basis points. These observations do not prove planning, mastery, personality, or spectator appeal.
5. Optionally choose one named built-in rival. A rival uses the same gameplay seed and exact configuration in an independent lane.
6. Choose `four-direction-step-v1` for one tool call per decision or `four-direction-burst-v1` for bounded straight continuations that stop at public decision events.
7. Optionally provide a public Agent Passport v4 with a caller-declared agent ID and policy version, bounded display name, and `avatar_id`, `accent_id`, and `station_id` selected from `vibesnake://agent/identity`. Its `symbolic-step-v4` observation profile and action profile must match the selected match profile. Unknown catalog IDs reject before a session is created. Never put prompts, reasoning, credentials, or personal data in a passport.
8. When live watching is requested, set `watchEnabled` to true. Give the returned capability only to the local same-user launcher, and do not persist, quote, or repeat its token. Live frames are best effort; explicitly save the verified replay if later viewing is wanted.
9. For an exhibition, call `start_match` with the action profile, optional passport, `styleContractId`, and `rivalPersonalityId`. Supply `gameplaySeed` as a quoted decimal string such as `"42"`; a JSON number is rejected before the tool runs. For a blind match, omit `gameplaySeed`. Keep `maximumSteps` at or below 2000.

## Practice in Signal School

Call `start_lesson` with one published lesson ID, an action profile, and an optional passport. Do not send a custom mode, seed, step cap, Style Contract, or rival. Each lesson owns its canonical open practice seed, mode, step cap, instruction, and ordered requirements under `ordered-replay-attempt-evidence-v2`.

- `first-turn`: submit the current direction's exact opposite once, confirm the zero-step `illegal_direction` rejection, then make a legal accepted turn. Use a fresh key for the legal turn. An exact retry does not add evidence, and stale, conflicting, capacity, or wrong-profile rejections do not qualify.
- `wrap-line`: produce a typed `wrapped` event whose same verified post-step state remains `running`.
- `hunger-route`: produce a verified `ate_food` step that does not end in starvation death.
- `exit-route`: grow on food and retain at least two structural non-reversing exits in the Running post-step state.
- `power-route`: collect one named power kind, then observe a verified activation for that same kind later in event order.
- `recover-route`: produce a typed `collision_prevented` event with its closed cause and protection power, and remain `running` in that verified post-step state.
- `combo-route`: produce at least three verified `ate_food` steps and reach a verified post-step peak combo of three.
- `death-read`: collect food until length five, then make consecutive left turns into the occupied body. Confirm that the verified terminal `dead` state and `died` event both report self-collision. Starvation exceeds this practice cap. This is attributable protocol evidence, not proof of private reasoning.

1. Read `lesson_progress.requirements`, `requirements_satisfied`, `first_unmet_requirement_id`, `evidence_state`, and the bounded attempt-evidence count and hash in every observation.
2. After each move or burst, read `lesson_delta` for newly satisfied requirement IDs. Exact retries return the identical cached delta and evidence hash. A rejection changes progress only when the lesson explicitly requires that exact current-state rejection witness.
3. A lesson burst stops when all requirements first become satisfied. Read `steps_advanced`, `stop_reason`, `stop_event`, and the refreshed progress before acting again.
4. When live progress reports `recommended_next_tool: finish_match`, call it instead of padding steps. Read `lesson_outcome` only from a verified result. It contains the ordered verified requirements, their replay-trace or attempt-witness evidence source, first unmet requirement, closed review code, replay payload hash, distinct attempt-evidence hash, and aggregate evidence hash. A reached target omits retry guidance; an unmet outcome includes a fresh-session retry descriptor.
5. On a verified miss, report the exact first unmet requirement and review code. On replay failure, no verified lesson outcome exists; follow the failed-closed retry guidance and call `start_lesson` again for a fresh session. Never resume the old state or reuse its handle or mutation keys.
6. Read the Signal School resource's measured interaction evidence. Action calls count only `play_move` and `play_burst`. UTF-8 bytes cover each exact camelCase MCP tool arguments object and snake_case structured response, excluding MCP or JSON-RPC framing, logs, viewer traffic, and token estimates. Bounded straight-line bursts use an observation-derived `maximumSteps` from 1 through 16. Every paired burst route must use no more action calls than its step route, and at least six of eight lessons must use fewer. These checked-in measurements are deterministic regression evidence, not token estimates or product-wide limits.
7. A completed practice is not mastery or qualification. Checked-in non-practice-seed fixtures test evaluator generalization; qualification-time decks and persistent curriculum history remain separate future work.

## Play one decision at a time

1. Read the latest observation. Coordinates use `x` increasing right and `y` increasing down.
2. Inspect the ordered body, food, visible pickup, detached obstacles, hunger, combo, effects, previous events, and remaining step budget.
3. Select only `up`, `right`, `down`, `left`, or `continue`.
4. Use `continue` to keep moving straight. Do not submit the current direction as a turn.
5. Reject plans that immediately reverse direction. The host advances zero steps for illegal or stale input.
6. Optionally set `declaredIntent` to `seek_food`, `seek_power`, `preserve_space`, `take_risk`, or `recover` so a human viewer can follow the public plan. Use `undeclared` when no label is accurate.
7. Call `play_move` with a new bounded idempotency key and the exact `tick` and `state_hash` from that observation.
8. If the request is rejected, inspect `rules_advanced` before assuming zero advancement, then use the refreshed observation. Preflight and logical rejections advance zero steps; a post-step `replay_failure` may report one real step and always fails closed. Do not blindly retry with a new key.
9. If transport delivery is uncertain, retry the identical request, including its declared intent, with the identical key. Never reuse a key for different input.
10. Repeat until `is_action_awaited` is false or a match result is returned.

## Use a bounded symbolic burst

Use `play_burst` only in a `four-direction-burst-v1` match when one initial turn followed by a straight continuation is safe.

1. Supply the exact current tick and state hash, a fresh idempotency key, one initial action, one public intent, and a `maximumSteps` value from 1 through 16.
2. The initial action applies once. Every later accepted step continues the resulting direction. The host does not accept an action array or a caller-defined stop expression.
3. The host stops at the first fixed public decision event, selected Signal School all-requirements transition, rules terminal, match step cap, replay failure, or requested bound. Read `steps_advanced`, `stop_reason`, `stop_event`, `lesson_delta`, final-step ordered events, and the refreshed observation before deciding again.
4. Use a short bound near visible food, powers, obstacles, hunger pressure, wraps, or crowded body cells. Use `play_move` in a step-profile match when every step needs deliberation. The live viewer labels a burst, its actual steps advanced, stop reason, stop event, and any coalesced earlier updates, while the verified replay remains the complete canonical history.
5. Retry uncertain delivery only with the identical complete burst request and identical key. A key is shared across step and burst mutations, so it can never name both.

One accepted `play_move` advances exactly one clock-free rules step. An accepted `play_burst` advances the authoritative `steps_advanced` count, from 1 through the requested bound. Response latency never affects score. `observe_match` and `get_match_result` never advance the game.
The rival advances once for each accepted agent step while its lane is running. Rejected agent input advances neither lane.

## Make decisions from public state

- Preserve at least one route away from the head as the body grows.
- Treat hunger and terminal survival as harder constraints than a distant reward.
- Re-evaluate after every food, power, wrap, protection, combo, hunger, or collision event.
- In a wrapping mode, evaluate neighbors modulo board width and height.
- Do not expect a safe-move recommendation. Collision reasoning is part of play.
- Do not reconstruct future spawns in a blind-seed match.
- Treat public intent as a short self-report for spectators. It does not affect the game or replace action selection.
- Treat the 30-minute live-session idle lease as resource reclamation, not a gameplay clock. At capacity, an inactive handle may expire without a result or replay. Viewer activity never keeps a match alive or ends it.
- Keep any explanation short and based on visible facts. Do not expose hidden chain of thought.

## Finish and hand off

Use the exact camelCase argument names and JSON types from the discovered tool schema. `play_move` requires `action`; `play_burst` requires `initialAction` and `maximumSteps`; `start_match` takes `gameplaySeed` as a quoted decimal string. The host rejects missing, unexpected, and wrong-typed argument names on every tool before game code runs, identifies the exact mismatch, lists the required and optional fields, and confirms that no match state changed. Correct the argument object instead of retrying the same payload. The rejection carries no observation because it never entered match code; call `observe_match` if separate proof of the unchanged tick and state hash is wanted.

Call `finish_match` after a Signal School observation reports that all requirements are satisfied. That produces a completed lesson result. In any other running match, call it only to stop early, which produces an aborted result. Normal terminal and step-limit endings finalize automatically.

Read `lifecycle` and `run_status` as answers to different questions. `lifecycle` describes the agent session as `awaiting_action`, `completed`, `aborted`, or `failed_closed`. `run_status` describes the snake as `running`, `dead`, or `won`. A `completed` lesson with `run_status: running` is the normal result of finishing a satisfied practice on a living snake. `is_action_awaited` stays true until you finish, because satisfying every requirement never ends a match. A requirement's `satisfied` flag reports that its closed evidence exists; it is not a grade. Then:

1. Call `get_match_result` if the terminal response did not include a result.
2. For a styled match, report the two live criterion values as observed until a result exists. Then report the replay-bound `style_outcome`, its exact numerator and denominator for rate criteria, score, final tick, end reason, run status, and replay verification code. `threshold_reached`, `thresholds_reached`, and `all_thresholds_reached` are measurements against optional style targets, not pass/fail match grades. Never turn a threshold crossing into a claim about intent or mastery.
3. Call `get_exhibition_receipt` for the canonical receipt of a successfully finalized, verified match. It publishes two identities. `receipt_hash` names this visit and binds the match handle, so a rematch always mints a new one. `route_identity_hash` names the walked line: the same division, seed, verified replays, and satisfaction outcome reproduce it across separate matches and separate host processes, so use it to recognise an already-walked route and to compare a same-seed rematch. `display_time_utc` is presentation-only and never part of either hash. A live, unverified, or failed-closed match reports `is_available: false`. Report the receipt hash rather than inventing a summary of the match.
4. Call `save_verified_replay` only when persistence or human viewing is desired. A rivalry saves both independently verified lane replays. Replay schema 1 stores accepted rules steps, not Signal School rejection witnesses; the verified lesson outcome remains available only with the retained host result until a future receipt persists both evidence domains.
5. Treat each replay payload hash and final state hash as lane verification identifiers. The exhibition receipt hash-links them into `receipt_hash` for this visit and `route_identity_hash` for the line; a persisted passport and league standings remain future work.
6. For a rematch, start a new open-seed match with the revealed seed. Never treat a previous handle as durable.

Agent matches are exhibitions. Do not claim that they update human scores, progression, achievements, or the built-in spectator league.

## Recover safely

- Unknown or no-longer-retained handle: start a new match. Completed handles and capacity-reclaimed idle live handles may be evicted, and every handle becomes invalid when the local host exits.
- Stale tick or state hash: act on the returned current observation.
- Illegal direction: choose a legal turn or `continue`; no step was lost.
- Idempotency conflict: create a new key only for a genuinely new action.
- Wrong action profile: use the tool named by the observation's immutable action profile.
- Mutation capacity exceeded: known exact retries remain valid, but no new mutation can be accepted; finish early or start a new match.
- Replay failure: no verified result exists; report the failed-closed outcome and start a new match.
- Host capacity: finish an existing live match or restart the local host.
- Replay save failure: preserve the verified lane result and report the bounded store error. Never supply or invent a filesystem path.
