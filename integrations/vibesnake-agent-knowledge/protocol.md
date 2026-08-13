---
type: "Protocol"
title: "Vibe Snake MCP agent host"
description: "The local stdio MCP surface and its portable Agent Plugin packaging."
tags: [vibesnake, mcp, agent-plugins, stdio]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-13T21:30:45Z }
verified: { by: process:vibesnake-quality-gate, at: 2026-08-13T21:30:56Z }
stale_after: "2026-11-13"
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

The host version is `0.2.0`. The Agent Plugin version is `0.2.1` and targets `https://agent-plugins.org/schemas/1.0.0/plugin.schema.json`.
The MCP server targets stable protocol `2026-07-28` through the official C# SDK `2.2.0`.
Clients must speak the stateless MCP `2026-07-28` era: every request carries protocol metadata, optional discovery uses `server/discover`, and there is no protocol session. Legacy `initialize` handshakes are rejected and this preview provides no downlevel fallback.

# Tools

* `finish_match`
* `get_match_result`
* `observe_match`
* `play_burst`
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

The first transport is local stdio. It opens no network listener, accepts no executable, arbitrary path, action list, or custom stop predicate, and keeps opaque bearer handles in one bounded process without a separate client-authentication layer. Finalized matches are evicted first at capacity; otherwise only a live handle with no valid handle-bearing operation for 30 minutes may be reclaimed without a result or replay. Replacement construction precedes eviction, and viewer activity is never match control. The normative Agent Plugins repository labels 1.0.0 Published while the public specification website still labels it Working Draft, so Vibe Snake retains preview-quality packaging and drift review.
