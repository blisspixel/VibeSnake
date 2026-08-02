# Project Tools

This directory contains explicit command-line tools. Runtime code belongs under `src/` or `game/`, deterministic tests belong under `tests/` or `native/tests/`, and generated outputs belong under ignored artifact directories.

## Required quality and release gates

| Tool | Ownership |
| --- | --- |
| `check_source_policy.py` | Executable anti-slop, Unicode, and Python placeholder policy |
| `lock_python_dependencies.py` | CI and player-runtime hash-lock freshness checks and explicit regeneration |
| `check_docs.py` | Canonical documentation discovery and relative-link validation |
| `capture_readme_screenshots.py` | Isolated current-build README screenshot capture and freshness verification |
| `visual_generate_badges.py` | Deterministic radio-station badge generation and byte verification |
| `visual_generate_logo.py` | Preferred brand-logo hash and dimension verification |
| `content_inventory.py` | Deterministic source-content inventory and release blocker report |
| `content_packs.py` | Core and optional pack manifest qualification |
| `assert_godot_toolchain.ps1` | SHA-512 archive, extracted editor SHA-256, and exact build identity verification |
| `native_artifact_policy.ps1` | Shared prohibited-path rules for native bundles |
| `platform_path_policy.ps1` | Absolute environment-path validation for tooling |
| `test_powershell_gates.ps1` | Executable-spoofing and artifact-path regression checks |
| `test_native.ps1` | Native rules, coverage, format, Godot import, and scene smoke |
| `test_native_export.ps1` | Exported player launch and artifact qualification |
| `inspect_native_artifact.ps1` | Payload rules, portability scan, and SHA-256 manifest |
| `install_godot.ps1` | Checksum-verified Godot editor bootstrap |
| `install_godot_templates.ps1` | Checksum-verified export-template bootstrap |

These entry points are kept at `scripts/` root because README, CI, and release documentation invoke them directly.

## Visual production

`visual_generate_badges.py` is deterministic and enforced by CI using project-owned
pixel glyphs. `visual_generate_logo.py` hash-checks the preferred handcrafted brand
mark under `assets/images/logo.png`.
`capture_readme_screenshots.py` renders documented game states from isolated
player and audio directories and checks exact current bytes.

Legacy credentialed audio-generation, grading, curation, rename, and mutation
programs are preserved only in the ignored local archive. They are not release
tools. A future audio-admission command must operate on an ignored candidate
workspace, default to analysis-only behavior, require explicit paid execution,
cap spend, preserve immutable inputs, pin external media tools, emit provenance,
and remain unable to write directly into public assets.

## Manual tools

- `manual/`: intentionally interactive or perceptual checks with no pytest collection side effects.

Retired executable source is removed instead of hidden behind lint or test exclusions. Historical decisions and reports belong in the ignored local archive; useful automatic behavior is rebuilt as deterministic tests or QA scenarios.
