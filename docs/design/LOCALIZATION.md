# Localization contract

Vibe Snake 1.0 requires English player text plus a localization-ready shell. Additional translated locales are optional and cannot ship until they pass the same automated and human review as English.

## Runtime ownership

`game/scripts/ShellLocalization.cs` owns stable shell copy IDs, English templates, named format parameters, and deterministic pseudo-localization. Rules and persistence state contain only stable domain or content IDs. Localized presentation text must not enter a rules hash, replay outcome, score category, save identity, or content manifest identity.

The default locale is `en`. The development-only `qps-ploc` locale can be enabled with `--pseudo-locale`. It accents supported characters, preserves named parameters exactly, adds at least 30 percent expansion, and adds visible boundary and padding markers. Unknown copy IDs, missing parameters, unexpected parameters, duplicates, line breaks in values, and oversized values fail closed.

## Input prompts

Input glyphs remain vector badges selected from the active keyboard or controller family. When text needs to include a glyph token, the token is a named parameter rather than translated copy. Pseudo-localization must preserve the parameter exactly. A locale cannot replace, remove, or reinterpret a bound input.

## Qualification

`localization-qualification-v1` is produced by the real Godot headless smoke and enforced by `scripts/test_native.ps1`. It currently proves:

- 647 unique schema-bound copy entries and 99 parameterized templates, including preview-only Agent Arena watch copy outside the supported 1.0 shell-flow set below.
- Thirteen shell flows migrated to stable IDs: menu, onboarding, settings, bindings, progression, Broadcast Tour, cosmetics, local scores, content packs, replays, interactive spectator mode, optional lore archive, and offline comparisons.
- All 18 Rules-owned onboarding IDs, 24 typed step-feedback IDs, and 24 Persistence-owned broadcast caption IDs resolve to exact English copy.
- Deterministic pseudo-localization with a measured minimum expansion ratio of 1.3125.
- Exact named-parameter rejection cases and unchanged input-glyph parameters.
- Zero missing fallback-font glyphs across the pseudo-locale catalog.
- Every pseudo-localized entry fits the 1280 by 720 logical canvas at the maximum 150 percent text scale.
- The seven-row Agent Arena watch overlay fits its actual shared geometry at 150 percent text using composed worst-case pseudo-localized survival, catalog avatar and station identity, either both ordered Style Contract criteria or both ordered Signal School requirements, their observed, verified, or unavailable evidence status, rival, burst, delivery, match state, intent, and rejection copy. A row that exceeds its measured width is middle-elided on Unicode grapheme boundaries so both its leading context and trailing result remain visible without clipping.
- The actual worst-case English Agent Arena survival, verification, and outcome rows keep every character at 150 percent text. Fitting a row by eliding it is not the same as a spectator reading it, so this gate is separate from the pseudo-localized geometry gate above.
- The ordinary run HUD title keeps every character at 150 percent text for every mode and status pairing, including `PAUSED: FOCUS LOST` and the preview `AGENT FAILED CLOSED`. The title has a hard right edge at the logical canvas, so it now declares an explicit width budget and shrinks its own type before it would ever drop a letter.

The evidence requires zero direct `DrawLabel` string literals, zero direct action/static prompt-caption literals, zero direct or composed status literals, and zero remaining audited domain-presentation expressions. Rules onboarding emits stable copy IDs, step feedback emits typed `ShellTextReference` values, and Persistence broadcast scheduling emits stable caption IDs. Composed run HUD values remain typed runtime data rather than translatable sentence templates. The automated V080-07 foundation is complete; visible keyboard and controller review on Windows, macOS, and Linux remains required for release closure.

## Translator handoff gate

Before adding a non-English release locale:

1. Freeze the English catalog and export ID, template, parameter, context, and layout-budget data.
2. Require exact ID and parameter parity with no fallback caused by missing translated entries.
3. Run pseudo-locale and candidate-locale glyph, expansion, maximum-text-scale, keyboard-prompt, and all controller-family prompt checks.
4. Review every screen on Windows, macOS, and Linux, including narrow aspect ratios and reduced-motion, flash-free, high-contrast, and muted profiles.
5. Obtain native-speaker review for meaning, tone, truncation, terminology, and accessibility wording.

No machine-generated translation is release-approved without the same native-speaker and visual review.
