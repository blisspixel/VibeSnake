# Audio System

## Runtime overview

Vibe Snake has two audio layers:

1. Event sound effects loaded by the game coordinator.
2. A radio manager that discovers MP3 playlists by station prefix and controls playback.

The radio network is a defining feature. Its locally preserved candidate library is also the largest rights, curation, and release-distribution problem.

## Local review inventory

The ignored local archive preserves 95 unique MP3 candidates under `archive/source-assets/audio/unverified-runtime/radio/`. When a developer explicitly sets `VIBESNAKE_AUDIO_DIR` to the parent `unverified-runtime` directory, the reference runtime assigns each path to exactly one station. Every candidate passed the bounded two-frame MPEG structure gate before isolation. Structure does not prove complete decode, listening quality, provider-aware provenance, or release approval.

| Station | Runtime key | Discovered paths | Non-empty candidates | Recognized prefixes |
| --- | --- | ---: | ---: | --- |
| The Flow Signal | `flow_signal` | 12 | 12 | `flow_signal_`, `ambient_`, `chill_` |
| Chaos Theory | `chaos_theory` | 12 | 12 | `chaos_theory_`, `jazz_` |
| The Global Coil | `global_coil` | 12 | 12 | `global_coil_`, `world_`, `soul_` |
| Ourotron | `ourotron` | 14 | 13 | `ourotron_`, `synthwave_` |
| The Pit | `the_pit` | 11 | 11 | `the_pit_`, `dance_` |
| The Bureau | `the_bureau` | 12 | 12 | `the_bureau_` |
| The Strike | `the_strike` | 13 | 11 | `the_strike_`, `rock_` |
| Underground Scales | `underground_scales` | 15 | 12 | `underground_scales_`, `hiphop_` |

The six empty radio paths and the empty test-audio file were removed. The clean public-source tree contains no MP3 or WAV candidate. The source alpha remains fully playable with deterministic procedural event cues and no radio pack. Local audition still skips unreadable files at playback, but curation must reject any decode failure rather than relying on that recovery path.

The mapping lives in [radio_manager.py](../../src/vibesnake/audio/radio_manager.py). Tests assert station discovery, cycling, track uniqueness, empty-library handling, and playback orchestration.

## Inventory authority

- Public runtime truth: no authored audio pack is approved; procedural event cues provide the clean-clone fallback.
- Local audition truth: files discovered by `RadioManager` beneath the explicit `VIBESNAKE_AUDIO_DIR` overlay.
- Production history: an ignored local inventory containing 186 historical
  records; it is evidence for candidate review, not live playlist or release
  authority.
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
isolated in the ignored archive; most event-to-sound integration is incomplete.
The next audio pass should define a reviewed matrix for UI navigation, food,
combo tiers, starvation warning, all nine power-ups, achievement unlock, pause,
restart, and death.

## Native qualification layer

The Godot slice now establishes the minimum audio boundary that later authored content must use:

- Music, SFX, and UI buses are registered beneath Master at startup.
- Confirm, back, pause, food, Shield spawn, Shield activation, Shield expiry, Shield break, death, and victory each use a short fallback generated once as a finite cached 16-bit stereo PCM WAV stream.
- UI actions and ordered rules events select cues without mutating deterministic run state. The Shield resolver uses one explicit priority order so collision recovery wins over activation, expiry, spawn, and food when events share a step.
- Shield events also produce persistent text captions, while the pickup marker, countdown, and active outline preserve the state when audio is muted.
- Headless and packaged-player smoke tests validate bus registration, cue construction, playback through the dummy backend, phased stop and release, and clean process exit. They fail on engine warnings, leaked objects, or a missing success marker.

These generated tones are engineering fallbacks, not final sound design. Finite PCM resources keep their lifetime explicit and avoid a continuously driven generator during shutdown. They ensure the core flow remains functional when authored content is missing or an optional pack is absent. The qualification smoke proves control flow and resource cleanup, not audible quality, physical-device compatibility, loudness, latency, or mix balance.

The next native audio work is ordered as follows:

1. Add Master, Music, SFX, and UI settings with independent mute and gain persistence.
2. Define a typed cue catalog with logical IDs, bus, priority, cooldown, polyphony, caption, visual fallback, and optional haptic metadata.
3. Extend the proven Shield mapping through starvation thresholds, combo tiers, remaining collision causes, restart, and the other eight power contracts.
4. Test missing files, unavailable output, device changes, rapid retriggering, pause, focus loss, and shutdown.
5. Select and normalize a minimal authored core cue set through the asset manifest.
6. Replace individual fallback tones only when an authored cue passes rights, decode, loudness, clipping, repetition, and listening review.
7. Add Voice and Accessibility buses when broadcast narration, cue captions, and player settings have executable contracts.

## Footprint and distribution

The clean-clone `assets/audio` tree contains one rights-cleared JSON cue catalog
and no audio binary. The ignored `archive/source-assets` collection contains 423 files totaling
740,801,845 bytes; the 95-track radio review library accounts for 338,592,122
bytes. The 1.0 delivery decision is a small approved core soundtrack plus one or
more optional radio packs. Every pack needs a versioned manifest, hashes, sizes,
compatibility, source, rights, provider-aware provenance, attribution, station
identity, track metadata, and explicit install or removal. The game never
downloads or replaces a pack without player action and always retains a complete
offline core experience. Source classification and approval rules are defined in
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
