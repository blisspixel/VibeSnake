# Known Issues

[Current status](STATUS.md) | [Roadmap](../../ROADMAP.md) | [Support](../../SUPPORT.md) | [Recovery](../guides/RECOVERY.md)

Status: pre-candidate alpha issues as of 2026-08-20. Replace this page from the exact candidate review before release.

## Player-facing limitations

- There is no versioned public native alpha or later release candidate yet. The root launchers run the native Godot source build; the optional Python player is a frozen behavior reference and does not contain every native feature.
- No radio or optional-content pack is export eligible. All 95 radio candidates fully decode without source changes, but zero pass the complete provisional loudness, true-peak, and silence policy as-is. Twelve lossless `the_bureau` review copies pass that technical policy and reproduce byte-for-byte. Their hash-bound headphone/speaker listening template is prepared, but all 12 decisions and every approval and export flag remain pending or false. Player artifacts must exclude the source library until exact replacements pass listening review and regenerated manifests, provenance, credits, and rights evidence agree.
- Hosted Windows x64, macOS Universal, and Linux x64 Debug exports pass automated outside-checkout qualification. The first clean three-platform Release dispatch found one shared `ExportRelease` compilation defect after the Agent Arena preview assembly was correctly removed. The fix and ordinary-CI regression gate are implemented. Exact [Release run 32421705560](https://github.com/blisspixel/VibeSnake/actions/runs/32421705560) now passes and retains all three unsigned platform packages at `e87db6e`, including 300 fresh-profile launches, 600,000 reliability comparisons, 300 spectator restarts, 21 injected faults, cross-platform matrix evidence, and three provenance bundles. A candidate-bound four-row review workspace is prepared from independently recomputed evidence, but all 144 physical cells, signing, notarization, Linux runtime-baseline review, and storefront delivery remain pending.
- Keyboard, mouse, D-pad, stick, Xbox-layout, and PlayStation-layout routes have automated coverage. Physical controller families, hot-plug combinations, pointer focus, and complete platform flows still need retained human evidence.
- Accessibility settings and structural layout gates are implemented, but visible focus, readability, photosensitivity, physical input, and accessibility-user review remain open.
- Performance evidence from shared headless runners is diagnostic only. Minimum and recommended hardware, target operating-system versions, both target resolutions, and long-session thermal and memory evidence are not yet published.
- Procedural fallback cues are complete and rights-clear. Authored production music and SFX remediation, mix, listening, speaker, headphone, and physical audio-device review remain open.
- Public support, issue, discussion, play-feedback, and enhancement intake is intentionally closed, and the matching GitHub features are disabled. Private vulnerability reporting is enabled, but its end-to-end acknowledgement and response flow still needs a controlled test. Do not publish private or security-sensitive information in a public channel.

## Qualification flakiness

- The `bare-arcade-loop` frame-pacing budget measures real p95 and maximum frame milliseconds on hosted runners. It has failed intermittently on `macos-latest` and then passed for the same commit on an unchanged concurrent run or a fresh-runner retry. A shared-runner stall can therefore exceed the 60 ms p95 or 100 ms maximum budget without a product regression. The budget remains deliberately unchanged because it guards a real player-facing promise. One bounded tail-only resample occurs inside the smoke, and any final failure names the exact budget and measured values. Consider a runner-aware measurement design if another recurrence blocks clean qualification; do not silently loosen the product budget.

## Data safety

- Uninstalling the application preserves player data. See the [recovery guide](../guides/RECOVERY.md) for intentional category reset, verified backup, restore, and complete local removal.
- A future-schema or corrupt document is preserved rather than silently downgraded. Keep the original for a compatible newer build or reviewed recovery.
- Optional pack removal and player-data reset are separate actions. A removed pack may remain in recoverable quarantine.

## Release-blocking evidence still absent

The exact candidate record and four manual-session templates are prepared, but the manual product matrix contains 0 of 144 retained platform-flow cells. The 12-track listening template is also prepared, but contains zero completed decisions. Controlled external validation contains zero candidates and participant sessions. Named-hardware performance, accessibility participation, authored-content approval, protected signing, selected-channel lifecycle, real cross-version rollback, current candidate screenshots and video, and final release-material claim matching remain pending.
