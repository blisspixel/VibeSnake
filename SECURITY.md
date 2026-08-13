# Security Policy

## Supported versions

Vibe Snake is currently an alpha. Security fixes are applied to the latest code
on the default branch. No released version has a long-term support commitment yet.

## Report a vulnerability

Private vulnerability reporting is enabled. Submit sensitive reports through the
repository's [private vulnerability form](https://github.com/blisspixel/VibeSnake/security/advisories/new).
Never include exploit details, private data, or unpatched vulnerabilities in a
public issue.

Include the affected revision, platform, reproduction steps, impact, and any safe
supporting evidence. Expect an acknowledgement after a maintainer has reviewed the
report. Public disclosure is coordinated only after a fix or documented mitigation
is available.

## Review scope

Security review covers source code, native and Python dependency boundaries,
content-pack parsing, save and replay parsing, build and release automation, and
official player artifacts. The post-1.0 Agent Arena preview adds local MCP input,
public observation projection, bounded session ownership, verified replay save,
portable plugin manifests, and same-user named-pipe authentication to that scope.
It does not cover modified third-party builds or
services outside project control.

The official preview opens no network listener, accepts no arbitrary filesystem
path, rules configuration, executable, prompt, credential, or agent-authored code,
and never loads third-party executable plugins into Godot. MCP clients and external
agent processes remain outside the game trust boundary. A one-time viewer token is
a local capability and is not a defense against software already running as the
same compromised user.

Optional agent public intent is a closed enum. It changes idempotent request
identity but has no rules, scoring, reward, replay, qualification, filesystem,
network, or execution authority.

The repository must never contain real credentials, signing material, private
reports, or player data. See the
[code quality standard](docs/engineering/CODE_QUALITY_STANDARDS.md) for enforced
engineering controls.
