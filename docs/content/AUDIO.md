# Audio System

## Runtime overview

Vibe Snake has two audio layers:

1. Event sound effects loaded by the game coordinator.
2. A radio manager that discovers MP3 playlists by station prefix and controls playback.

The radio network is a defining world-building feature: eight diegetic stations,
host identity, and a full offline playlist you can flip through like GTA radio
while you play.

## Public radio inventory

The clean public-source tree includes 95 original MP3 tracks under
`assets/audio/radio/`. `RadioManager` assigns each path to exactly one station by
filename prefix. All eight stations are populated in a normal clone.

| Station | Runtime key | Tracks | Recognized prefixes |
| --- | --- | ---: | --- |
| The Flow Signal | `flow_signal` | 12 | `flow_signal_`, `ambient_`, `chill_` |
| Chaos Theory | `chaos_theory` | 12 | `chaos_theory_`, `jazz_` |
| The Global Coil | `global_coil` | 12 | `global_coil_`, `world_`, `soul_` |
| Ourotron | `ourotron` | 13 | `ourotron_`, `synthwave_` |
| The Pit | `the_pit` | 11 | `the_pit_`, `dance_` |
| The Bureau | `the_bureau` | 12 | `the_bureau_` |
| The Strike | `the_strike` | 11 | `the_strike_`, `rock_` |
| Underground Scales | `underground_scales` | 12 | `underground_scales_`, `hiphop_` |

The mapping lives in [radio_manager.py](../../src/vibesnake/audio/radio_manager.py).
Tests assert station discovery, cycling, track uniqueness, empty-library
handling, and playback orchestration. Procedural SFX fallbacks still cover
missing event cues when individual SFX files are absent.

Packaged native releases do not use filename-prefix discovery. `RadioCatalog`
projects station and track metadata only from strict, inventory-validated radio
pack manifests. `OptionalPackStore` validates every installed file, allowlist,
size, and SHA-256 before returning a station. For normal play from a source
checkout, a development bridge maps the same reviewed Python prefix table into
an in-memory catalog and loads only the current track. Smoke and launch probes
disable that bridge, and exports never include it as pack authority.
`RadioPlaybackPolicy` then owns
shuffle, no immediate repeat when alternatives exist, explicit single-track
repeat, exact-position pause/resume delegation, last-track-from-start station
retune, end-of-track advance, mute/help state, and missing-track or missing-pack
recovery on a separately injected radio PCG stream. The Godot adapter reads and
decodes only the current verified MP3 on the Music bus. `J` and controller `R3`
cycle stations, while menu, run, and Content Packs expose bounded station,
track, pack, mute, and recovery-help text.

## Inventory authority

- Public runtime truth: the eight-station radio catalog under `assets/audio/radio/` plus procedural event-cue fallbacks.
- Override truth: an explicit `VIBESNAKE_AUDIO_DIR` overlay for local experiments.
- File, integrity, and release authority: [content_inventory.json](../../config/content_inventory.json) plus [content_policy.json](../../config/content_policy.json).
- Creative plan: [config/radio_network_plan.json](../../config/radio_network_plan.json).
- Canonical lore and broadcast grammar: [World and broadcast bible](../design/WORLD_BIBLE.md).

## Sound effects

The main game defines four direct authored-content paths:

- Eat.
- Loss.
- Magnet.
- Fallback music.

Missing or unreadable eat, loss, and magnet cues fall back to short deterministic
16-bit PCM chirps generated in memory. Background music remains silent without an
approved pack. Additional local SFX candidates and retired production scripts are
isolated in the ignored archive; authored event-sound admission remains incomplete.
The native feedback matrix now defines the complete policy for UI navigation,
food, score/combo state, starvation warning, powers, achievements, pause,
restart, recovery, and death. Authored files remain absent until approval.

## Native qualification layer

The Godot slice now establishes the minimum audio boundary that later authored content must use:

- Music, SFX, and UI buses are registered beneath Master at startup.
- The schema-7 preferences document retains the mono-output field introduced in schema 4. It enables one named `AudioEffectStereoEnhance` at the end of Master with side-channel gain set to zero, so the engine mixer downmixes every current and future child bus before platform output while the disabled path leaves stereo unchanged.
- Navigate, confirm, back, pause, restart, achievement, food, four combo tiers, combo break, starvation warning, both death causes, victory, power lifecycle/recovery, and nine one-to-one power activations each use a short fallback generated once as a finite cached 16-bit stereo PCM WAV stream.
- UI actions and ordered rules events select cues without mutating deterministic run state. The Shield resolver uses one explicit priority order so collision recovery wins over activation, expiry, spawn, and food when events share a step.
- Shield events also produce persistent text captions, while the pickup marker, countdown, and active outline preserve the state when audio is muted.
- Optional playback failures cannot escape into gameplay. A pure recovery tracker deduplicates unavailable/restored notices, keeps a persistent `AUDIO UNAVAILABLE: VISUAL CUES ACTIVE` status visible, writes sparse local diagnostics, waits one bounded monotonic interval, repairs missing buses, and recovers on a later cue.
- A playback-free `AudioMixAllocator` owns monotonic voice leases, per-bus capacity, per-cue polyphony, cooldown groups, priority, stable lower-priority interruption, expiry, and strongest-active music ducking. Identifiers, buses, voices, cooldown groups, times, and request ranges are bounded. The Godot player applies the closed 31-cue policy through 8 SFX voices and 4 UI voices instead of stopping the previous cue.
- `sfx-catalog-qualification-v1` requires a unique runtime ID and PCM SHA-256 for every cue, exact deterministic-runtime provenance and Apache-2.0 license metadata, stereo 22.05 kHz PCM, a measured -24.5 to -18.0 dBFS procedural peak window, no clipping, all nine power activations, and distinct navigation, combo, restart, achievement, and death identities. The future authored target is -18 LUFS integrated and -1 dBTP. No authored file is claimed to meet it yet.
- Saved Music, SFX, and UI volumes apply immediately and independently. Transient ducking changes only the Music bus and restores its saved base gain when the last ducking lease ends. A bounded one-second output-topology probe stops stale voices and reapplies the bus graph and saved settings after an output change.
- Headless and packaged-player smoke tests validate bus registration, single-instance mono configuration and toggling, every cue, playback-free cooldown/polyphony/priority/interruption decisions, real bus routing and music duck/restore, immediate saved-volume isolation, output-device polling and repair, 992 rapid retriggers, full-catalog muted playback suppression, injected missing-bus failure, retry backoff, recovery, bounded voices and caching, phased stop/release, rules-state isolation, and clean process exit. CI requires the resulting settings and `audio-mixing-policy-v2` JSON evidence and fails on engine warnings, leaked objects, or a missing success marker.
- `feedback-matrix-qualification-v1` maps all 19 ordered rules events and 15 shell-action families to a dominant channel, visual cue, audio policy, text and haptic alternatives, priority, cooldown, bounded polyphony, stacking/interruption, music ducking, shake, flash, hitstop, criticality, accessibility alternatives, and explicit implementation/asset state. All 31 fallback cues are accounted for. No authored native feedback asset is approved or silently implied.
- `radio-behavior-qualification-v1` requires validated-manifest projection, complete station/track/pack metadata, shuffle/no-repeat, single-track repeat, pause/resume, station retune, end-of-track advance, mute/help state, missing-track recovery, missing-pack core continuity, packaged inventory, keyboard/controller cycling, decoder-adapter presence, and rules/gameplay-RNG isolation. Twelve focused native policy tests cover the same boundary without an audio device.
- `broadcast-qualification-v1` requires complete identities for all eight planned stations, explicit unapproved content state, four safe host boundaries, ordinary-combo track continuity, boundary-specific ducking, critical-cue interruption priority, caption fallback, a 100-step cooldown, an eight-segment run cap, per-station host no-repeat bags, adaptive-layer refusal without compatible material, and radio/gameplay-RNG plus rules-state isolation. The radio selector also exhausts every playable station track before refill and prevents an immediate repeat across the refill boundary.

These generated tones are engineering fallbacks, not final sound design. Finite PCM resources keep their lifetime explicit and avoid a continuously driven generator during shutdown. They ensure the core flow remains functional when authored content is missing or an optional pack is absent. The qualification smoke runs through Godot's Dummy backend and proves control flow, mono-effect configuration, bounded retry, mute behavior, and resource cleanup. It does not prove audible mono output, physical-device compatibility or hot-swap behavior, loudness, latency, or mix balance.

The remaining native audio work is ordered as follows:

1. Add retained physical-device unplug, default-device change, pause/focus, and audible latency/listening observations on Windows, macOS, and Linux. Automated polling/repair, missing-bus, retry, rapid-retrigger, mute, shutdown, and rules-isolation coverage is complete.
2. Select and normalize a minimal authored core cue set through the asset manifest.
3. Replace individual fallback tones only when an authored cue passes rights, decode, loudness, clipping, repetition, and listening review.
4. Add Voice and Accessibility buses only when approved narration exists. Broadcast captions and player-facing fallback policy already have executable contracts and remain active without audio.

## Footprint and distribution

The public `assets/audio` tree contains the rights-cleared cue-metadata JSON and
the full eight-station offline radio library (95 MP3 tracks under
`assets/audio/radio/`). Those tracks are rights-cleared for project distribution
and still blocked for native pack export until loudness, listening, credit, and
allowlist gates pass. The 1.0 delivery decision remains a small approved core
soundtrack plus one or more optional radio packs with versioned manifests,
hashes, sizes, compatibility, rights, attribution, station identity, track
metadata, and explicit install or removal. The game never downloads or replaces
a pack without player action and always retains a complete offline core
experience. Source classification and approval rules are defined in
[CONTENT_PIPELINE.md](CONTENT_PIPELINE.md), and the executable manifest and
failure-isolation rules are defined in [CONTENT_PACKS.md](CONTENT_PACKS.md).

The target Godot audio system uses streamed tracks and explicit Master, Music, SFX, UI, Voice, and Accessibility buses. Music, SFX, and UI currently exist as qualification buses; Voice and Accessibility remain planned. The authored radio, Vibe Level, host, stinger, ducking, repetition, and experience policy is defined in [FUN_DESIGN.md](../design/FUN_DESIGN.md#radio-as-a-reactive-world). Station institutions, host voices, canon, and broadcast grammar are defined in the [world and broadcast bible](../design/WORLD_BIBLE.md#the-serpentine-broadcast-network) and staged in the [roadmap](../../ROADMAP.md).

## Production-tool boundary

Runtime play installs the hashed [runtime lock](../../requirements-runtime.lock).
No credentialed audio-generation or model-based grading dependency is part of
the public source graph. Historical production scripts and their dependency
lock are preserved only in the ignored local archive because they do not meet
the current safety, cost, reproducibility, media-analysis, or evidence contract.

The next public audio-admission tool must be one narrow command that defaults to
read-only analysis, requires an explicit execution flag for paid work, enforces
a declared cost ceiling, writes only to an ignored candidate workspace,
preserves source bytes, pins every decoder and normalizer build, produces
machine-readable measurements and provenance, and cannot approve content by
itself. Human rights and listening decisions remain separate signed records.

## Safe production workflow

1. Work on copies or generated candidates in an ignored directory outside the public asset tree.
2. Record source, generator, model or service, prompt version, license, duration, loudness, and checksum.
3. Normalize and listen on headphones and speakers.
4. Check station fit and duplicate content.
5. Move only rights-cleared and quality-approved runtime files into the canonical asset tree.
6. Run radio tests and the full suite.
7. Update this inventory table if counts change.

Never store API keys in `.py`, JSON inventories, reports, prompts, or documentation.
