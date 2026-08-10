# Known Issues

[Current status](STATUS.md) | [Roadmap](../../ROADMAP.md) | [Support](../../SUPPORT.md) | [Recovery](../guides/RECOVERY.md)

Status: pre-candidate alpha issues as of 2026-08-10. Replace this page from the exact candidate review before release.

## Player-facing limitations

- There is no public 0.9 or 1.0 native release candidate. The root launchers run the native Godot source build; the optional Python player is a frozen behavior reference and does not contain every native feature.
- No radio or optional-content pack is export eligible. The public source inventory contains candidates, but player artifacts must exclude them until manifest, provenance, media, listening, credit, and rights gates pass.
- Native Windows x64 Release export passes local automated qualification. Final retained Windows, macOS Universal, and Linux x64 candidate artifacts, signing, notarization, Linux runtime-baseline review, and storefront delivery are pending.
- Keyboard, mouse, D-pad, stick, Xbox-layout, and PlayStation-layout routes have automated coverage. Physical controller families, hot-plug combinations, pointer focus, and complete platform flows still need retained human evidence.
- Accessibility settings and structural layout gates are implemented, but visible focus, readability, photosensitivity, physical input, and accessibility-user review remain open.
- Performance evidence from shared headless runners is diagnostic only. Minimum and recommended hardware, target operating-system versions, both target resolutions, and long-session thermal and memory evidence are not yet published.
- Procedural fallback cues are complete and rights-clear. Authored production music, SFX, loudness, mix, listening, speaker, headphone, and physical audio-device review remain open.
- Public support, issue, conduct, and vulnerability intake routes are not yet open and tested. Do not publish private or security-sensitive information in a public channel.

## Data safety

- Uninstalling the application preserves player data. See the [recovery guide](../guides/RECOVERY.md) for intentional category reset, verified backup, restore, and complete local removal.
- A future-schema or corrupt document is preserved rather than silently downgraded. Keep the original for a compatible newer build or reviewed recovery.
- Optional pack removal and player-data reset are separate actions. A removed pack may remain in recoverable quarantine.

## Release-blocking evidence still absent

The manual product matrix contains 0 of 144 retained platform-flow cells. Controlled external validation contains zero candidates and participant sessions. Named-hardware performance, accessibility participation, authored-content approval, protected signing, selected-channel lifecycle, real cross-version rollback, current candidate screenshots and video, and final release-material claim matching remain pending.
