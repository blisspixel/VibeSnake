# Security Policy

## Supported versions

Vibe Snake is currently an alpha. Security fixes are applied to the latest code
on the default branch. No released version has a long-term support commitment yet.

## Report a vulnerability

Private vulnerability reporting is not enabled on the empty GitHub repository
yet. Do not publish source or accept external contributions until it is enabled
and a maintainer has tested the private report flow from the repository's
[Security page](https://github.com/blisspixel/VibeSnake/security). Never include
exploit details, private data, or unpatched vulnerabilities in a public issue.

Include the affected revision, platform, reproduction steps, impact, and any safe
supporting evidence. Expect an acknowledgement after a maintainer has reviewed the
report. Public disclosure is coordinated only after a fix or documented mitigation
is available.

## Review scope

Security review covers source code, native and Python dependency boundaries,
content-pack parsing, save and replay parsing, build and release automation, and
official player artifacts. It does not cover modified third-party builds or
services outside project control.

The repository must never contain real credentials, signing material, private
reports, or player data. See the
[code quality standard](docs/engineering/CODE_QUALITY_STANDARDS.md) for enforced
engineering controls.
