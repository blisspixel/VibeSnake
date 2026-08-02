# Source Assets

This directory contains rights-cleared source candidates and production metadata for the Python reference and future native content packs.

| Directory | Purpose |
| --- | --- |
| `ai/` | Built-in, example, and custom AI personality data |
| `audio/` | Rights-cleared production metadata; public runtime audio is intentionally absent until approved |
| `config/` | Runtime configuration overlay |
| `images/` | Logo and station badges |

File presence does not imply release approval. `config/content_policy.json` and the generated `config/content_inventory.json` own classification, integrity, rights, pack assignment, and export eligibility. The native artifact must contain exact manifest allowlists, not a recursive copy of this directory.

Rejected tracks, rights-unverified audio, generated candidates, working stems, copied research, and analysis output belong in the ignored local `archive/source-assets/` tree. They are deliberately absent from a clean clone and from this canonical inventory. The Python reference synthesizes small gameplay cue fallbacks when approved files are absent.
