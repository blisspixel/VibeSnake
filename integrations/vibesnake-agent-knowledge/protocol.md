---
type: "Protocol"
title: "Vibe Snake MCP agent host"
description: "The local stdio MCP surface and its portable Agent Plugin packaging."
tags: [vibesnake, mcp, agent-plugins, stdio]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-13T00:00:00Z }
verified: { by: process:vibesnake-ci, at: 2026-08-13T00:00:00Z }
stale_after: "2026-11-13T00:00:00Z"
status: draft
sources:
  - id: mcp-tools
    resource: ../../native/tools/VibeSnake.AgentHost/McpAgentTools.cs
    title: "MCP tool adapter"
  - id: mcp-resources
    resource: ../../native/tools/VibeSnake.AgentHost/AgentResources.cs
    title: "MCP resources"
  - id: plugin-manifest
    resource: ../vibesnake-agent-plugin/plugin.json
    title: "Agent Plugin manifest"
  - id: agent-plugins-spec
    resource: https://agent-plugins.org/specification
    title: "Agent Plugins 1.0.0 specification"
---
# Versions

The host version is `0.1.0`. The Agent Plugin version is `0.1.0` and targets `https://agent-plugins.org/schemas/1.0.0/plugin.schema.json`.
The MCP server targets stable protocol `2026-07-28` through the official C# SDK `2.2.0`.
Clients must initialize with exactly MCP `2026-07-28`; legacy initialize revisions are rejected and this preview provides no downlevel fallback.

# Tools

* `finish_match`
* `get_match_result`
* `observe_match`
* `play_move`
* `save_verified_replay`
* `start_match`

# Resources

* `vibesnake://agent/modes`
* `vibesnake://agent/playbook`
* `vibesnake://agent/rivals`
* `vibesnake://agent/rules`
* `vibesnake://agent/signal-school`
* `vibesnake://agent/styles`

# Trust boundary

The first transport is local stdio. It opens no network listener, accepts no executable or arbitrary path, and keeps opaque handles in one bounded process. Agent Plugins packaging is preview-quality because its 1.0.0 specification remains a working draft.
