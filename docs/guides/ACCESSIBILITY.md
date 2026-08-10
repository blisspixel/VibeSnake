# Accessibility Feature Guide

This guide describes the accessibility features implemented in the native Godot and C# candidate. It does not claim that the temporary Python reference has the same settings. Accessibility validation is still in progress, so this page separates automated evidence from review that requires people and physical hardware.

## Supported features

| Area | Current native support | Automated evidence |
| --- | --- | --- |
| Text size | Shell text scales from 85 to 150 percent. At 150 percent, long catalogs use paging and required settings, bindings, and achievement rows stay within the 1280 by 720 logical canvas. | Fallback-font dimensions and layout budgets are checked in `shell_presentation.json`. |
| Contrast | Standard important text targets at least 4.5:1, essential non-text UI targets at least 3:1, and the high-contrast primary palette targets at least 7:1. Current measured primary ratios are 15.86:1 standard and 21:1 high contrast. | Palette calculations are checked in `shell_presentation.json`. |
| Focus and state | Selection, binding capture, conflicts, bound or unbound state, and achievement state use distinct text or shape markers in addition to color. | Marker uniqueness and catalog navigation are checked in `shell_presentation.json`. Human visible-focus review remains required. |
| Keyboard-only use | Required movement, menu, settings, pause, replay, and recovery routes use logical keyboard actions. Direction uses arrow keys or WASD by default. | Raw keyboard routes and keyboard input cadence are checked in the packaged smoke. |
| Controller-only use | Required movement, menu, settings, pause, replay, and recovery routes use logical controller actions. Direction accepts D-pad or left stick by default. | Raw controller routes plus D-pad and stick cadence are checked in the packaged smoke. Physical controller-family review remains required. |
| Mouse use | Nine scaled menu targets support start and major browsers. Left confirms or steers relative to the snake head, right performs Back, middle pauses, and wheel axes navigate. Letterbox input is rejected. | `mouse_input.json` drives menu, settings, start, gameplay, and return through live Godot input. Physical mouse and pointer-focus review remain required. |
| Remapping | Keyboard and controller primary bindings can be changed independently. A remap preserves the opposite device class. Conflicts offer an explicit swap or lossless cancel, and safe defaults can be restored. | Keyboard and controller remap, conflict, persistence, and restoration paths are checked before `settings_screen.json` is written. |
| Single-action navigation | Direction, Confirm, and Back are sufficient for required menu and settings navigation. No required navigation step depends on pressing two controls together. | Sequential raw keyboard and controller routes are checked in `settings_screen.json`. |
| Controller drift protection | Stick deadzone is adjustable from 10 to 90 percent. D-pad remains digital at every setting, and low-amplitude passive stick motion is rejected. | Settings and nine input-cadence cases cover the boundary. |
| Audio separation | Master, Music, SFX, and UI each have independent level and mute controls. Muting audio does not remove critical text or shape feedback. | Bus isolation, immediate saved levels, mute paths, and multimodal fallbacks are checked. |
| Mono output | A single Master-bus downmix makes the complete mix available through either output channel. | The settings smoke requires exactly one applied mono effect. Physical listening remains required. |
| Visual alternatives | Hunger, combo, powers, protection, and death use text and stable shapes or symbols as well as color and sound. Collision and starvation have distinct attribution and recovery text. | `multimodal_feedback.json` covers default, muted, reduced-motion, flash-free, and minimum-effects-muted profiles. |
| Reduced motion | Reduced motion disables nonessential motion and forces effective screen shake to zero. Static combo, state, and warning signals remain. | Accessibility and multimodal profile matrices check these decisions without changing rules state. |
| Flash safety | The native presentation policy permits no full-screen flashes in any profile. Flash-free mode also removes rapid emphasis, forces effective shake to zero, and lengthens critical caption time without muting audio. | Four accessibility profiles and five multimodal profiles check the policy. Human photosensitivity review remains required. |

## Display and text-scale support

The native shell owns a 1280 by 720 logical canvas, preserves aspect ratio, and uses letterboxing or pillarboxing when needed. It clamps an undersized window to an effective 640 by 360 surface. The automated maximum-text-scale audit crosses 150 percent text with these eight supported display classes:

| Display class | Requested size | Effective size |
| --- | ---: | ---: |
| Minimum clamp | 320 by 180 | 640 by 360 |
| HD 16:9 | 1920 by 1080 | 1920 by 1080 |
| Classic 4:3 | 1024 by 768 | 1024 by 768 |
| Desktop 16:10 | 1920 by 1200 | 1920 by 1200 |
| Ultrawide 21:9 | 3440 by 1440 | 3440 by 1440 |
| Square 1:1 | 1024 by 1024 | 1024 by 1024 |
| High-density 4K | 3840 by 2160 | 3840 by 2160 |
| High-density 5K | 5120 by 2880 | 5120 by 2880 |

This structural matrix proves scaling, mapping, bounds, and logical layout. Retained screenshots and readable-focus review on Windows, macOS, and Linux are still required before release.

## Configure the native game

Open Settings with F1 or controller Start, then choose Accessibility for contrast, motion, text scale, screen shake, and flash-free options. Choose Audio for independent volumes, mute controls, and mono output. Choose Controls to set stick deadzone, edit keyboard or controller bindings, or restore safe defaults.

Direct keyboard shortcuts are available for the protective settings:

| Shortcut | Action |
| --- | --- |
| F4 | Toggle flash-free mode |
| F5 or F6 | Decrease or increase text scale |
| F7 | Toggle Master mute |
| F9 | Toggle high contrast |
| F10 | Toggle reduced motion |
| F12 | Open local diagnostics |

## Release acceptance boundary

An inaccessible required flow is a P1 release blocker. Automated evidence does not establish that a feature is comfortable, readable, audible, or usable for a particular person. The candidate still requires:

- Retained visible audit on Windows, macOS, and Linux.
- Maximum-text-scale platform captures.
- Physical keyboard-only and controller-only required-flow review.
- Candidate review by players who use relevant accessibility settings.
- Human focus, contrast, readability, audio, and photosensitivity review.

Public accessibility intake is not open during the current alpha. Before release, [support](../../SUPPORT.md) must name a tested route. A future report should include the build version, operating system, display size, input device, settings profile, required flow, and observed result. Do not include private paths or personal medical information.
