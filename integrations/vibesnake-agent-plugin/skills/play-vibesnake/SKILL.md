---
name: play-vibesnake
description: Play deterministic Vibe Snake matches and canonical Signal School practice through the local MCP host, pursue a declared Style Contract, challenge a seed, and save a verified replay for a human to watch. Use when asked to play, practice, evaluate, or spectate Vibe Snake through the start_match, start_lesson, observe_match, play_move, play_burst, finish_match, get_match_result, or save_verified_replay tools.
---

# Play Vibe Snake

Treat the MCP tool schemas and returned observations as authoritative. Use this skill for play strategy and the safe match workflow. Never infer hidden state or ask for private reasoning.

## Prepare

1. Read `vibesnake://agent/rules`, `vibesnake://agent/modes`, and `vibesnake://agent/identity` once per host version. Read `vibesnake://agent/styles`, `vibesnake://agent/rivals`, or `vibesnake://agent/signal-school` when using those experiences.
2. Choose `classic` for fixed rules or `vibe` for the declared adaptive policy.
3. Choose `open` to receive the seed during play or `blind` to receive it only in the result.
4. Choose one style for the run. Only the preview's primary metric is scored; the route language is strategic flavor:
   - Stillwater: survive 200 steps while prioritizing open space.
   - Crownchaser: reach a peak combo of 4 while preserving continuity.
   - Edge Prophet: produce 3 typed near-miss events while using controlled edge risk.
   - Mutagenist: activate 2 powers without sacrificing survival.
   - Redline: collect 6 food while seeking direct routes under pressure.
5. Optionally choose one named built-in rival. A rival uses the same gameplay seed and exact configuration in an independent lane.
6. Choose `four-direction-step-v1` for one tool call per decision or `four-direction-burst-v1` for bounded straight continuations that stop at public decision events.
7. Optionally provide a public Agent Passport with a caller-declared agent ID and policy version, bounded display name, and `avatar_id`, `accent_id`, and `station_id` selected from `vibesnake://agent/identity`. Its action profile must match the selected match profile. Unknown catalog IDs reject before a session is created. Never put prompts, reasoning, credentials, or personal data in a passport.
8. When live watching is requested, set `watchEnabled` to true. Give the returned capability only to the local same-user launcher, and do not persist, quote, or repeat its token. Live frames are best effort; explicitly save the verified replay if later viewing is wanted.
9. For an exhibition, call `start_match` with the action profile, optional passport, `styleContractId`, and `rivalPersonalityId`. For a blind match, omit `gameplaySeed`. Keep `maximumSteps` at or below 2000.

## Practice in Signal School

Call `start_lesson` with one published lesson ID, an action profile, and an optional passport. Do not send a custom mode, seed, step cap, Style Contract, or rival. Each lesson owns its canonical open practice seed, mode, step cap, and primary public metric target.

1. Read `lesson_progress` in the initial and refreshed observations.
2. After each move or burst, read `lesson_delta` for factual progress made by that accepted mutation. Exact retries return the identical cached delta. Rejections have zero progress.
3. Finish normally or early and read `lesson_outcome` from the verified result. It binds the final primary-metric result to the verified replay payload hash.
4. Report `target_reached` or the exact `shortfall`. A reached practice target is not mastery, qualification, or completion of the planned eight-behavior curriculum.
5. Retry by starting the same lesson again. Persistent curriculum history and unseen-seed qualification are not part of this preview contract.

## Play one decision at a time

1. Read the latest observation. Coordinates use `x` increasing right and `y` increasing down.
2. Inspect the ordered body, food, visible pickup, detached obstacles, hunger, combo, effects, previous events, and remaining step budget.
3. Select only `up`, `right`, `down`, `left`, or `continue`.
4. Use `continue` to keep moving straight. Do not submit the current direction as a turn.
5. Reject plans that immediately reverse direction. The host advances zero steps for illegal or stale input.
6. Optionally set `declaredIntent` to `seek_food`, `seek_power`, `preserve_space`, `take_risk`, or `recover` so a human viewer can follow the public plan. Use `undeclared` when no label is accurate.
7. Call `play_move` with a new bounded idempotency key and the exact `tick` and `state_hash` from that observation.
8. If the request is rejected, use the refreshed observation in the response. Do not blindly retry with a new key.
9. If transport delivery is uncertain, retry the identical request, including its declared intent, with the identical key. Never reuse a key for different input.
10. Repeat until `is_action_awaited` is false or a match result is returned.

## Use a bounded symbolic burst

Use `play_burst` only in a `four-direction-burst-v1` match when one initial turn followed by a straight continuation is safe.

1. Supply the exact current tick and state hash, a fresh idempotency key, one initial action, one public intent, and a `maximumSteps` value from 1 through 16.
2. The initial action applies once. Every later accepted step continues the resulting direction. The host does not accept an action array or a caller-defined stop expression.
3. The host stops at the first fixed public decision event, selected Signal School target transition, rules terminal, match step cap, replay failure, or requested bound. Read `steps_advanced`, `stop_reason`, `stop_event`, `lesson_delta`, final-step ordered events, and the refreshed observation before deciding again.
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

Call `finish_match` only to stop early. Normal terminal and step-limit endings finalize automatically. Then:

1. Call `get_match_result` if the terminal response did not include a result.
2. Report the selected style or lesson outcome, score, final tick, end reason, run status, and replay verification code.
3. Call `save_verified_replay` only when persistence or human viewing is desired. A rivalry saves both independently verified lane replays.
4. Treat each replay payload hash and final state hash as lane verification identifiers. The preview does not yet produce the planned hash-linked public exhibition receipt.
5. For a rematch, start a new open-seed match with the revealed seed. Never treat a previous handle as durable.

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
