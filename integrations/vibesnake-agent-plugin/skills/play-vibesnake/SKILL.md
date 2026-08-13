---
name: play-vibesnake
description: Play deterministic Vibe Snake matches through the local MCP host, pursue a declared Style Contract, challenge a seed, and save a verified replay for a human to watch. Use when asked to play, practice, evaluate, or spectate Vibe Snake through the start_match, observe_match, play_move, finish_match, get_match_result, or save_verified_replay tools.
---

# Play Vibe Snake

Treat the MCP tool schemas and returned observations as authoritative. Use this skill for play strategy and the safe match workflow. Never infer hidden state or ask for private reasoning.

## Prepare

1. Read `vibesnake://agent/rules` and `vibesnake://agent/modes` once per host version. Read `vibesnake://agent/styles`, `vibesnake://agent/rivals`, or `vibesnake://agent/signal-school` when using those experiences.
2. Choose `classic` for fixed rules or `vibe` for the declared adaptive policy.
3. Choose `open` to receive the seed during play or `blind` to receive it only in the result.
4. Choose one style for the run:
   - Stillwater: prioritize survival and open space.
   - Crownchaser: preserve combo continuity.
   - Edge Prophet: use wrapping and controlled edge risk.
   - Mutagenist: route toward visible powers without sacrificing survival.
   - Redline: seek direct, efficient routes under pressure.
5. Optionally choose one named built-in rival. A rival uses the same gameplay seed and exact configuration in an independent lane.
6. Optionally provide a public Agent Passport with a stable agent ID, policy version, display name, color, shed, and station affinity. Use only the supported symbolic-step observation and four-direction-step action profiles. Never put prompts, reasoning, credentials, or personal data in a passport.
7. Call `start_match` with the optional passport, `styleContractId`, and `rivalPersonalityId`. For a blind match, omit `gameplaySeed`. Keep `maximumSteps` at or below 2000.

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

One accepted call advances exactly one clock-free rules step. Response latency never affects score. `observe_match` and `get_match_result` never advance the game.
The rival advances once for each accepted agent step while its lane is running. Rejected agent input advances neither lane.

## Make decisions from public state

- Preserve at least one route away from the head as the body grows.
- Treat hunger and terminal survival as harder constraints than a distant reward.
- Re-evaluate after every food, power, wrap, protection, combo, hunger, or collision event.
- In a wrapping mode, evaluate neighbors modulo board width and height.
- Do not expect a safe-move recommendation. Collision reasoning is part of play.
- Do not reconstruct future spawns in a blind-seed match.
- Treat public intent as a short self-report for spectators. It does not affect the game or replace action selection.
- Keep any explanation short and based on visible facts. Do not expose hidden chain of thought.

## Finish and hand off

Call `finish_match` only to stop early. Normal terminal and step-limit endings finalize automatically. Then:

1. Call `get_match_result` if the terminal response did not include a result.
2. Report the style, score, final tick, end reason, run status, and replay verification code.
3. Call `save_verified_replay` only when persistence or human viewing is desired. A rivalry saves both independently verified lane replays.
4. Treat each replay payload hash and final state hash as lane verification identifiers. The preview does not yet produce the planned hash-linked public exhibition receipt.
5. For a rematch, start a new open-seed match with the revealed seed. Never treat a previous handle as durable.

Agent matches are exhibitions. Do not claim that they update human scores, progression, achievements, or the built-in spectator league.

## Recover safely

- Unknown or no-longer-retained handle: start a new match. Completed handles may be evicted for capacity, and every handle becomes invalid when the local host exits.
- Stale tick or state hash: act on the returned current observation.
- Illegal direction: choose a legal turn or `continue`; no step was lost.
- Idempotency conflict: create a new key only for a genuinely new action.
- Host capacity: finish an existing live match or restart the local host.
- Replay save failure: preserve the verified lane result and report the bounded store error. Never supply or invent a filesystem path.
