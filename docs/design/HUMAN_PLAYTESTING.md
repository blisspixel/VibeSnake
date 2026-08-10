# Structured human playtesting

Status: V070-06 protocol-qualified, experience-unverified (2026-08-08).

This is the executable human-observation protocol for the 0.7 balance and control gate. The machine-readable authority is [`config/qa_human_playtest_protocol.json`](../../config/qa_human_playtest_protocol.json). Automated qualification proves that the build is reproducible and that the protocol is complete. It does not prove comprehension, feel, fairness, recovery payoff, fatigue, or desire to replay.

The retained automated handoff must say `automated-qualified-experience-unverified` until real formative, targeted follow-up, and fresh validation sessions are reviewed. No human target range may be introduced before that review.

## Privacy and consent

- Record participants only as session-local IDs matching `session-[0-9]{3}`.
- Keep consent records outside the repository and separate from observation data.
- Do not record names, accounts, email or physical addresses, IP addresses, device serials, system paths, raw input events, raw input timing, or unrelated device information.
- Sharing an in-game local playtest-summary export is optional and requires a separate participant choice. Declining does not affect the session.
- Do not commit raw participant records, voice, video, consent forms, or identifying free text to the public repository. Retain only a reviewed, de-identified aggregate when publication is appropriate.
- Quote only text the participant explicitly agreed may be retained, and remove contextual identifiers before review.

## Entry gate

Run `pwsh -NoProfile -File scripts/test_native.ps1` on the exact build before a session block. The run must pass all tests, the current 90 percent line and 85 percent branch module floors, and the Godot smoke. `TestResults/native/human_playtest_handoff.json` must then report:

- protocol hash matching the reviewed configuration;
- all eleven required automated artifact paths present, including `power_decisions.json`;
- zero invented human sessions;
- `experienceVerified: false` and `humanTargetRangesEstablished: false`;
- exact application, rules, mode, score, config, seed, artifact, platform, input, and accessibility identity fields required for each later session.

Do not begin or continue human sessions when a known severity-1 issue exists. Do not begin fresh validation while a severity-1 or severity-2 issue is unresolved.

## Cohorts and stages

Every complete study cycle covers these four cohorts:

| Cohort | Qualification |
| --- | --- |
| `first-time-keyboard` | No prior Vibe Snake play; completes the flow without a controller. |
| `first-time-controller` | No prior Vibe Snake play; completes the flow without gameplay keyboard input. |
| `returning-arcade` | Regularly plays score-focused arcade games and may have prior Vibe Snake exposure. |
| `accessibility-focused` | Uses or evaluates at least one muted, reduced-motion, flash-free, or high-contrast profile. |

Run stages in this order:

1. `formative` discovers comprehension, control, attribution, recovery, and replay-motivation problems.
2. `targeted-follow-up` repeats each affected scenario after a material repair. Participants may return when comparison is useful.
3. `fresh-validation` runs the complete flow with people who did not see the earlier builds or findings.

The protocol intentionally has no arbitrary participant-count shortcut. Continue until no unaddressed repeated critical pattern remains, every material repair has targeted follow-up, and a fresh cohort confirms the repaired flow. The same independently observed issue in two or more sessions is a repeated pattern.

## Session setup

Freeze and record these values before observation begins:

| Identity | Required value |
| --- | --- |
| Build | `appVersion`, `sourceRevision`, `artifactSha256`, `platform` |
| Rules | `rulesetId`, `rulesVersion`, `modeId`, `modeVersion`, `scoreCategoryId`, `configHash`, `seed` |
| Interaction | `inputDeviceClass`, `accessibilityProfileIds` |
| Participant boundary | `participantId`, `cohortId`, `stageId`, prior Vibe Snake exposure, input-experience band |

Use a clean or explicitly documented profile state. Do not coach controls, mode meaning, death cause, settings location, or recovery behavior before the scenario designed to observe that understanding. A facilitator may stop for safety, privacy, hardware failure, or a severity-1 defect.

## Shared scenario order

| Scenario | Seed | Observe without interpretation |
| --- | --- | --- |
| `first-launch` | None | Initial comprehension, chosen route, visible errors |
| `tutorial` | None | Lesson comprehension, chosen route, visible errors |
| `mode-selection` | None | Classic/Vibe understanding, selection route, visible errors |
| `seeded-run` | `42` | Control learning, routes, errors, spontaneous feedback |
| `death-attribution` | `7` | Stated death cause, uncertainty, incorrect attribution |
| `deliberate-restart` | `7` | Restart success, accidental or failed actions |
| `settings-discovery` | None | Whether and how relevant settings are found |
| `fixed-seed-recovery` | `20260808` | Anticipation, attribution, control, trade-off, willingness to retry |
| `voluntary-replay` | None | Whether another run starts without prompting and the stated reason |
| `boost-phase-shift` | `0` | Power identification, visibility, route detour, and explanation of the mobility synergy |
| `slow-mo-magnet` | `1` | Power identification, visibility, route detour, and explanation of the control/harvest synergy |
| `bait-boost` | `7` | Power identification, visibility, route detour, and explanation of the risk/tempo synergy |
| `gluttony-magnet` | `42` | Power identification, visibility, route detour, and explanation of the harvest synergy |
| `segment-detach-protection` | `20260808` | Geometry/protection identification, route detour, synergy explanation, and save attribution |
| `last-stand-long-combo` | `32452843` | Held protection visibility, route detour, save and death-adjacency attribution, and recovery control after a long combo |

The seeds come from the reviewed QA corpora. A targeted repair may add a reviewed regression seed, but must not silently replace these shared anchors.

## Recovery matrix

Repeat the fixed-seed recovery observation under all six profiles across the study cycle:

| Profile | Required boundary |
| --- | --- |
| `default` | Standard presentation and available audio |
| `muted` | No audio reliance |
| `reduced-motion` | Reduced-motion presentation active |
| `flash-free` | Flash-free presentation active |
| `high-contrast` | High-contrast presentation active |
| `controller-only` | Controller navigation and play without gameplay keyboard input |

Ask only after the observed attempt whether the recovery was anticipated, attributable, controllable, worth attempting again, and understood as a trade-off. Record the participant's explanation before discussing intended behavior.

## Observation record

An observation states what happened, not why the team thinks it happened. Each session record must preserve all applicable protocol fields and use explicit `not-observed` values when a scenario did not exercise a field. Required observation families are:

- comprehension;
- observed errors;
- chosen routes;
- death attribution;
- restart success;
- settings discovery;
- qualitative feedback;
- recovery anticipation, attribution, control, and willingness to retry;
- voluntary replay and the participant's stated skill goal or unresolved curiosity.
- power type identification and offer/active-state visibility;
- intentional power detours and the participant's synergy explanation;
- power save and death-adjacency attribution when the scenario exercises them.

Good observation: `session-004 opened Audio twice, returned, then found Reduced motion under Accessibility after 41 seconds.`

Invalid interpretation in an observation field: `The Accessibility section is confusing.`

Interpretation belongs in the separate review record, linked to the exact observations that support it.

## Review and decisions

Classify each finding:

| Severity | Meaning |
| --- | --- |
| `severity-1` | Safety, privacy, data-loss, or complete control failure that blocks all testing and release. |
| `severity-2` | Critical flow, input, attribution, accessibility, or recovery failure with no reasonable workaround. |
| `severity-3` | Material confusion, friction, or fatigue with a discoverable workaround. |
| `severity-4` | Minor preference, polish, or isolated wording observation. |

For each reviewed finding, retain separate fields for observation references, interpretation, proposed change, affected scenario and profile, decision, owner, verification scenario, and resolution state. The only decisions are `keep`, `revise`, `remove`, or `blocked`.

Do not tune a balance family from one anecdote or merely to raise average score. V070-07 requires a stated player-experience hypothesis, a target range written before the change, one changed family, the fixed corpus, the relevant human scenario, and a keep or revert decision.

## Exit criteria

The V070-06 human gate remains open until all of these are true:

1. Every cohort completes the shared flow with exact build and rules identity retained.
2. Every material repair receives targeted follow-up.
3. No severity-1 or severity-2 finding is unresolved.
4. No repeated critical pattern is unaddressed.
5. A fresh validation cohort that did not see earlier builds confirms the repaired flow.
6. Default and all five alternate recovery profiles have reviewed observation.
7. Negative and neutral outcomes remain in the evidence instead of being discarded.
8. Observation, interpretation, and product decision remain separately attributable.
9. Any human target range records the reviewed evidence and decision that introduced it.
10. All six power scenarios have reviewed route-choice, readability, synergy, save, and death-adjacency evidence where applicable.
11. The Mutation Fork has an evidence-backed `keep` or `remove` decision and cannot ship merely because its automated prototype works.

Until then, documentation must say `experience-unverified`, and the roadmap must not claim the human milestone complete.
