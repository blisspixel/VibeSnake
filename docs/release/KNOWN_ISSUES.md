# Known Issues

[Current status](STATUS.md) | [Roadmap](../../ROADMAP.md) | [Support](../../SUPPORT.md) | [Recovery](../guides/RECOVERY.md)

Status: pre-candidate alpha. Historical Release packages are superseded by later product changes. Replace this page from the exact candidate review before release.

## Concurrent profile writers

Preferences, tutorial progress, input bindings, achievements, progression, personal bests, score history, spectator league, and local playtest summaries use independent flushed staging and check on-disk version metadata before staging and replacement. Unsupported or ambiguous version declarations are preserved, including when another application version publishes them while a write is staged. These checks do not provide a cross-process lock between the final check and replacement, and concurrent supported-version updates can still overwrite each other's changes. Use one application instance per profile when retaining progress matters. Explicit summary deletion retains its existing cleanup of legacy temporary files and does not remove another writer's unique staging file.

## Player-facing limitations

- There is no versioned public native alpha or later release candidate yet. The root launchers run the native Godot source build; the optional Python player is a frozen behavior reference and does not contain every native feature.
- No radio or optional-content pack is export eligible. All 95 radio candidates fully decode without source changes, but zero pass the complete provisional loudness, true-peak, and silence policy as-is. Twelve lossless `the_bureau` review copies pass that technical policy and reproduce byte-for-byte. Their hash-bound headphone/speaker listening template is prepared, but all 12 decisions and every approval and export flag remain pending or false. Player artifacts must exclude the source library until exact replacements pass listening review and regenerated manifests, provenance, credits, and rights evidence agree.
- Hosted Windows x64, macOS Universal, and Linux x64 Debug exports pass automated outside-checkout qualification. The first clean three-platform Release dispatch found one shared `ExportRelease` compilation defect after the Agent Arena preview assembly was correctly removed. The fix and ordinary-CI regression gate are implemented. Historical [Release run 32421705560](https://github.com/blisspixel/VibeSnake/actions/runs/32421705560) passes and retains all three unsigned platform packages at `e87db6e`, including 300 fresh-profile launches, 600,000 reliability comparisons, 300 spectator restarts, 21 injected faults, cross-platform matrix evidence, and three provenance bundles. Later player changes supersede those bytes for physical acceptance. A fresh Release matrix and candidate-bound workspace are required; physical execution, signing, notarization, Linux runtime-baseline review, and storefront delivery remain pending.
- Keyboard, mouse, D-pad, stick, Xbox-layout, and PlayStation-layout routes have automated coverage. Physical controller families, hot-plug combinations, pointer focus, and complete platform flows still need retained human evidence.
- Accessibility settings and structural layout gates are implemented, but visible focus, readability, photosensitivity, physical input, and accessibility-user review remain open.
- Performance evidence from shared headless runners is diagnostic only. Minimum and recommended hardware, target operating-system versions, both target resolutions, and long-session thermal and memory evidence are not yet published.
- Procedural fallback cues are complete and rights-clear. Authored production music and SFX remediation, mix, listening, speaker, headphone, and physical audio-device review remain open.
- Public support, issue, discussion, play-feedback, and enhancement intake is intentionally closed, and the matching GitHub features are disabled. Private vulnerability reporting is enabled, but its end-to-end acknowledgement and response flow still needs a controlled test. Do not publish private or security-sensitive information in a public channel.

## Qualification flakiness

- The `bare-arcade-loop` frame-pacing budget measures real average, p95, and maximum frame milliseconds on hosted runners. One unchanged `macos-latest` run passed at 48.43 ms p95 while three others recorded 64.27 ms, 60.80 ms, and 62.11 ms. A first-envelope miss now captures three identical bursts and uses the per-frame minimum to remove one-sided scheduler delay before applying the unchanged 25 ms average, 60 ms p95, and 100 ms maximum gates. Every raw burst summary is retained, and a regression present across replicates remains fatal. Shared-runner evidence still cannot replace named-hardware acceptance.

## Data safety

- Uninstalling the application preserves player data. See the [recovery guide](../guides/RECOVERY.md) for intentional category reset, verified backup, restore, and complete local removal.
- A future-schema or corrupt document is preserved rather than silently downgraded. Keep the original for a compatible newer build or reviewed recovery.
- Optional pack removal and player-data reset are separate actions. A removed pack may remain in recoverable quarantine.

## Release-blocking evidence still absent

The historical candidate record and four manual-session templates are superseded. A fresh content-bearing Release matrix and schema-2 physical workspace are required before recording acceptance: 144 platform-flow cells, 432 complete-device flow cells, 16 mouse-capability cells, and 32 platform-profile cells. The 12-track listening template remains current but contains zero completed decisions. Controlled external validation contains zero candidates and participant sessions. Named-hardware performance, accessibility participation, authored-content approval, protected signing, selected-channel lifecycle, real cross-version rollback, current candidate screenshots and video, and final release-material claim matching remain pending. Native implementation and defect fixes continue while these release gates are open.
