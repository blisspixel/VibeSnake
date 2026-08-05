# Input and Application Lifecycle

This document separates the playable Python alpha controls from the native 1.0 input contract. Player-facing controls for the alpha remain in [PLAYER_GUIDE.md](../guides/PLAYER_GUIDE.md). The native shell is a qualification slice and is not yet the default runtime.

## Native action contract

Godot submits logical actions to the application boundary. Gameplay rules receive only normalized directions. They never inspect keys, controller buttons, axes, focus state, or raw engine events.

| Logical action | Keyboard defaults | Controller defaults | Current behavior |
| --- | --- | --- | --- |
| `vibe_move_up` | Up or W | D-pad Up or left stick up | Queue Up during an active, unpaused run |
| `vibe_move_right` | Right or D | D-pad Right or left stick right | Queue Right during an active, unpaused run |
| `vibe_move_down` | Down or S | D-pad Down or left stick down | Queue Down during an active, unpaused run |
| `vibe_move_left` | Left or A | D-pad Left or left stick left | Queue Left during an active, unpaused run |
| `vibe_confirm` | Enter or Space | Controller South | Start or restart only after active replay work finishes |
| `vibe_back` | Escape | Controller East | Return from a run or ending; request a save-aware exit from the main menu |
| `vibe_pause` | P | Start | Pause or resume an active run |
| `vibe_replay` | R | Controller North | Verify the latest stored native replay from the menu or ending |
| `vibe_quit` | Command or Control plus Q | Not assigned | Request an exit that gives an active replay save one bounded drain window |
| `vibe_restore_defaults` | F8 | Controller Select/Back | Rewrite keyboard and controller binding documents to defaults and re-apply the InputMap |
| `vibe_toggle_master_mute` | F7 | Not assigned | Toggle master mute and persist preferences |
| `vibe_toggle_high_contrast` | F9 | Not assigned | Toggle high-contrast presentation and persist preferences |
| `vibe_toggle_reduced_motion` | F10 | Not assigned | Toggle reduced-motion presentation and persist preferences |
| `vibe_toggle_fullscreen` | F11 | Not assigned | Toggle preferred fullscreen mode (interactive sessions only) and persist preferences |
| `vibe_volume_up` | `=` or keypad `+` | Not assigned | Raise master volume by 0.05, unmute master if muted, clamp to 1.0, and persist |
| `vibe_volume_down` | `-` or keypad `-` | Not assigned | Lower master volume by 0.05, clamp to 0.0, and persist |
| `vibe_text_scale_up` | F6 | Not assigned | Raise text scale by 0.05, clamp to 1.5, and persist |
| `vibe_text_scale_down` | F5 | Not assigned | Lower text scale by 0.05, clamp to 0.85, and persist |
| `vibe_toggle_flash_free` | F4 | Not assigned | Toggle flash-free presentation and persist preferences |
| `vibe_open_diagnostics` | F12 | Not assigned | Ensure and open the local diagnostics folder (headless no-op) |

Controller mappings use the engine's standardized button and axis names and accept any connected controller. The left-stick deadzone is 0.5 in the qualification slice. Shipping deadzones and per-device calibration remain settings work. Accessibility toggles are available on every screen and do not require a dedicated settings menu. Full remapping UI and glyph prompts remain open.

## Deterministic direction policy

- The rules queue holds at most three accepted directions by default.
- A direction equal to the effective direction is rejected.
- A 180-degree reversal against the effective buffered direction is rejected.
- At most one accepted direction is consumed per fixed rules step.
- Presentation frame rate and input repeat never advance gameplay directly.
- Paused runs reject movement actions instead of hiding them for later execution.

The direction queue is part of canonical state and the deterministic state hash. Native replays record every logical direction attempt by rules step rather than raw input events or frame timestamps, including attempts rejected by the queue. [REPLAYS.md](../engineering/REPLAYS.md) defines the capture and verification contract.

## Focus and lifecycle policy

Losing application focus during a running game pauses immediately and displays `PAUSED: FOCUS LOST`. Movement received while paused is ignored. The player must deliberately use Pause or controller Start after returning. Back clears the current qualification run and returns to the menu.

One replay file may be dropped onto the menu or ending for read-only inspection. A drop during an active run is rejected so file access cannot interrupt scored play. Multiple-file drops are rejected rather than choosing an ambiguous source.

Replay inspection and storage run off the main thread, one operation at a time.
Confirm cannot start a run while replay work is active. If a terminal save ever
arrives behind an inspection, it is retained and begins as soon as that inspection
finishes. Quit, menu Back, and operating-system window close wait for an active or
queued save, but a monotonic five-second deadline guarantees that a blocked
filesystem cannot make exit hang forever. Unexpected tree teardown uses the same
bounded save-drain policy. The full persistence and replay contract is in
[REPLAYS.md](../engineering/REPLAYS.md).

Godot owns controller discovery and hot-plug delivery. Because defaults target any joypad rather than startup index zero, a newly connected mapped controller can submit the same actions. The pure `ControllerConnectionTracker` records connect and disconnect events with sanitized captions; the shell seeds currently connected pads at launch, shows a connection notice on the menu, and pauses a live run when the last controller disconnects. Prompt-family switching, glyphs, device-specific fallback mappings, and multi-controller hardware evidence still remain before 1.0.

## Remapping and accessibility requirements

The current native defaults are registered centrally in [GameActions.cs](../../game/scripts/GameActions.cs). They do not yet provide a player-facing remapping screen or persistence. The shipping system must add:

- Schema-versioned action remaps with migration and reset-to-default.
- Conflict detection that never strands Confirm, Back, Pause, or required movement.
- Keyboard-only and controller-only completion of every required flow.
- Device-family prompts that change after deliberate input, not passive axis noise.
- Adjustable stick deadzones and a digital fallback.
- Single-action navigation, visible focus, and alternatives for hold or repeated actions.
- Safe controller disconnect behavior during a run.

## Automated proof

The real Godot headless and packaged-player smoke verifies that every required action has at least one default binding. It then uses logical action events to start a run, buffer movement, pause on focus loss, reject hidden movement while paused, resume, advance the rules, return to the menu, and verify the latest isolated replay. Replay lifecycle smoke also proves run-start gating, terminal-save retention behind inspection, release after successful save, and deadline release for a save task that never completes. The pure C# suite separately proves queue capacity, invalid-turn rejection, canonical queue restoration, live attempt capture, deterministic mirror comparison, generated operation sequences, and replay storage failures.

Physical controller hardware, multiple simultaneous controllers, platform prompt families, focus transitions from an actual window manager, remapping UI, and accessibility review still require native runner or human evidence. They are not implied by the headless smoke.

## Important files

- [GameActions.cs](../../game/scripts/GameActions.cs): logical names and default engine bindings.
- [Main.cs](../../game/scripts/Main.cs): screen-specific action routing, focus pause, and smoke coverage.
- [SnakeRun.cs](../../native/src/VibeSnake.Rules/SnakeRun.cs): bounded deterministic direction queue.
- [SnakeRun.Restore.cs](../../native/src/VibeSnake.Rules/SnakeRun.Restore.cs): strict canonical queue restoration.
- [TECHNOLOGY_STRATEGY.md](../decisions/TECHNOLOGY_STRATEGY.md): complete cross-platform input requirements.
- [ROADMAP.md](../../ROADMAP.md): versioned remapping, controller, accessibility, and validation work.
