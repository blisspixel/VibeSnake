# Input and Application Lifecycle

This document defines the native Godot product input contract and preserves the frozen Python mappings only where migration or parity work needs them. Player-facing native controls are in [PLAYER_GUIDE.md](../guides/PLAYER_GUIDE.md). The Godot shell is the default source runtime; Python is an optional behavior oracle.

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
| `vibe_replay` | R | Controller North | Open replay browse from menu/ending; reset active playback; return from playback or offline comparisons |
| `vibe_quit` | Command or Control plus Q | Not assigned | Request an exit that gives an active replay save one bounded drain window |
| `vibe_restore_defaults` | F8 | Controller Select/Back | Restore bindings on binding/settings surfaces; prepare exact selected-item deletion in replay or household-rival browsers |
| `vibe_toggle_master_mute` | F7 | Not assigned | Toggle master mute and persist preferences |
| `vibe_toggle_high_contrast` | F9 | Not assigned | Toggle high-contrast presentation and persist preferences |
| `vibe_toggle_reduced_motion` | F10 | Not assigned | Toggle reduced-motion presentation and persist preferences |
| `vibe_toggle_fullscreen` | F11 | Not assigned | Toggle preferred fullscreen mode (interactive sessions only) and persist preferences |
| `vibe_volume_up` | `=` or keypad `+` | Not assigned | Raise master volume by 0.05, unmute master if muted, clamp to 1.0, and persist |
| `vibe_volume_down` | `-` or keypad `-` | Not assigned | Lower master volume by 0.05, clamp to 0.0, and persist |
| `vibe_text_scale_up` | F6 | Not assigned | Raise text scale by 0.05, clamp to 1.5, and persist |
| `vibe_text_scale_down` | F5 | Not assigned | Lower text scale by 0.05, clamp to 0.85, and persist |
| `vibe_toggle_flash_free` | F4 | Not assigned | Toggle flash-free presentation and persist preferences |
| `vibe_open_diagnostics` | F12 | Not assigned | Ensure diagnostics folder, copy absolute path to clipboard, and open the folder (open is headless no-op) |
| `vibe_browse_achievements` | U | Left shoulder | Open achievements from menu/ending; open lore from spectator selection; open offline comparisons from replays; explicitly import into the selected household slot from comparisons |
| `vibe_browse_bindings` | B | Right shoulder | Open schema-1 keyboard/controller bindings. Left/Right selects the device class, Up/Down selects an action, Confirm starts capture, Back cancels or returns, and F8 restores defaults from any screen |
| `vibe_browse_content_packs` | C | Controller West | Open content packs from menu/ending; export a verified replay bundle or household run card on the matching browser; start the spectator seed challenge after a finished broadcast |
| `vibe_browse_settings` | F1 | Start | Open settings from menu/ending; return from settings |

Controller mappings use the engine's standardized button and axis names and accept any connected controller. Keyboard keys, controller buttons, and deliberate controller-axis movement can be captured and persisted. Axis motion below 0.75 is ignored during capture so passive stick drift cannot claim a binding. Preferences schema 7 exposes one shared gameplay-stick deadzone from 0.10 through 0.90 in 0.05 steps, keeps D-pad buttons digital at every threshold, and persists the Vibe adaptation opt-out plus default-off local playtest consent. Xbox, PlayStation, Nintendo, and generic prompt families switch only after a deliberate key, button, or strong-axis event. Asset-free vector badges render on menu, run-end, achievements, bindings, content-packs, replays, settings, onboarding, spectator, lore, and offline comparison surfaces, with readable text retained inside every badge. Per-device calibration, physical-device, and visual review remain open.

Mouse input uses the same logical action boundary. Nine explicit main-menu targets cover start, customization, achievements, local scores, spectator channels, replays, settings, optional Help, and quit. Left click confirms or activates a target, right click performs Back, middle click pauses or resumes a run, vertical wheel input navigates Up or Down, and horizontal wheel input changes the selected mode while pointing at Start. During an unpaused run, left click chooses the dominant direction from the snake head to the scaled logical pointer. Letterbox and pillarbox input is rejected. Mouse activity does not rewrite keyboard or controller bindings.

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

Godot owns controller discovery and hot-plug delivery. Because defaults target any joypad rather than startup index zero, a newly connected mapped controller can submit the same actions. The pure `ControllerConnectionTracker` records connect and disconnect events with sanitized captions; the shell seeds currently connected pads at launch, shows a connection notice on the menu, and pauses a live run or active replay when the last controller disconnects. Deliberate input selects the prompt family without allowing passive axis noise to change it. Device-specific fallback review and multi-controller hardware evidence still remain before 1.0.

## Remapping and accessibility requirements

The current native defaults are registered centrally in [GameActions.cs](../../game/scripts/GameActions.cs). The player-facing bindings screen supports persisted schema-1 keyboard and controller remaps, physical-axis conflict handling, and reset-to-default. A free token replaces the selected action immediately. A token owned by another action opens an explicit conflict state where Confirm atomically swaps the two actions and Back/Escape cancels without changing either binding. Confirm, Back, and restore-defaults remain reachable throughout the flow. The shipping system must still add:

- Migration handling when a future schema version changes the stable binding vocabulary.
- Retained physical evidence for keyboard-only and controller-only completion of every required flow.
- Retained physical evidence for device-family prompts after deliberate input.
- Per-device deadzone calibration if physical-device evidence shows the shared setting is insufficient.
- Human review of visible focus and alternatives for hold or repeated actions after automated non-color marker and pagination proof.
- Human review of controller disconnect clarity during live runs and playback.

## Automated proof

The real Godot headless and packaged-player smoke verifies that every required action has a default binding and that keyboard/controller remaps reach InputMap without dropping the opposite device. It proves drift rejection, conflict ownership, lossless cancel, atomic swap, default restoration, and binding round-trip. `input-cadence-qualification-v1` maps real key, D-pad, and stick events through the live mapper; all nine device/cadence cases consume the same five rapid turns exactly once and finish with the same rules hash. `mouse-input-qualification-v1` executes scaled menu targeting, settings navigation, horizontal and vertical wheel actions, run start, gameplay direction, Back, letterbox rejection, and binding isolation through the live `_Input` route. `settings-screen-qualification-v1` completes the six-section route with raw keyboard and controller events, migrates and round-trips schema-7 preferences, applies the shared deadzone, retains D-pad fallback, configures mono once, toggles Vibe adaptation into its isolated category, restores defaults, and surfaces save failure. `local-playtest-summary-qualification-v1` adds raw keyboard consent and controller deletion routes. The same logical actions cover onboarding, run start, buffered movement, pause/focus safety, content packs, replay browse/playback, and return paths. The pure C# suite separately proves token vocabularies, prompt-family detection, device rejection, conflict identity, atomic swaps, queue capacity, onboarding, replay behavior, bounded storage, and failure handling.

Physical keyboard, mouse, and controller hardware, multiple simultaneous controllers, pointer hover and visible-focus behavior, focus transitions from an actual window manager, illustrated prompt assets, and accessibility review still require native runner or human evidence. They are not implied by the headless smoke.

## Important files

- [GameActions.cs](../../game/scripts/GameActions.cs): logical names and default engine bindings.
- [Main.cs](../../game/scripts/Main.cs): screen-specific action routing, focus pause, and smoke coverage.
- [SnakeRun.cs](../../native/src/VibeSnake.Rules/SnakeRun.cs): bounded deterministic direction queue.
- [SnakeRun.Restore.cs](../../native/src/VibeSnake.Rules/SnakeRun.Restore.cs): strict canonical queue restoration.
- [TECHNOLOGY_STRATEGY.md](../decisions/TECHNOLOGY_STRATEGY.md): complete cross-platform input requirements.
- [ROADMAP.md](../../ROADMAP.md): versioned remapping, controller, accessibility, and validation work.
