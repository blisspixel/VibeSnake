---
type: "Protocol"
title: "Vibe Snake MCP agent host"
description: "The local stdio MCP surface and its portable Agent Plugin packaging."
tags: [vibesnake, mcp, agent-plugins, stdio]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-15T11:31:47Z }
verified: { by: process:vibesnake-quality-gate, at: 2026-08-15T11:31:47Z }
stale_after: "2026-11-14"
status: draft
sources:
  - id: mcp-tools
    resource: ../../native/tools/VibeSnake.AgentHost/McpAgentTools.cs
    title: "MCP tool adapter"
  - id: mcp-resources
    resource: ../../native/tools/VibeSnake.AgentHost/AgentResources.cs
    title: "MCP resources"
  - id: viewer-contract
    resource: ../../native/src/VibeSnake.AgentPlay/AgentViewer.cs
    title: "Live viewer wire contract"
  - id: viewer-client
    resource: ../../native/src/VibeSnake.AgentViewer/AgentViewerClient.cs
    title: "Live viewer client"
  - id: plugin-manifest
    resource: ../vibesnake-agent-plugin/plugin.json
    title: "Agent Plugin manifest"
  - id: agent-plugins-normative-spec
    resource: https://raw.githubusercontent.com/agentplugins/agent-plugins-spec/1fc1b6270e3cc492ec2d24ad7a34277c6d53b9c1/spec/1.0.0.md
    title: "Immutable Agent Plugins 1.0.0 normative specification"
  - id: agent-plugins-website
    resource: https://agent-plugins.org/specification
    title: "Agent Plugins public specification website"
  - id: mcp-specification
    resource: https://modelcontextprotocol.io/specification/2026-07-28
    title: "Model Context Protocol 2026-07-28 specification"
  - id: mcp-csharp-sdk
    resource: https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0
    title: "Official C# SDK 2.2.0 release"
---
# Versions

The host version is `0.13.0`. The Agent Plugin version is `0.13.0` and targets `https://agent-plugins.org/schemas/1.0.0/plugin.schema.json`.
The MCP server targets stable protocol `2026-07-28` through the official C# SDK `2.2.0`.
Clients must speak the stateless MCP `2026-07-28` era: every request carries protocol metadata, optional discovery uses `server/discover`, and there is no protocol session. Legacy `initialize` handshakes are rejected and this preview provides no downlevel fallback.

# Tools

* `archive_exhibition`
* `finish_match`
* `forget_exhibition`
* `get_exhibition_receipt`
* `get_match_result`
* `list_exhibitions`
* `observe_match`
* `play_burst`
* `play_move`
* `save_verified_replay`
* `start_lesson`
* `start_match`

# Resources

* `vibesnake://agent/identity`
* `vibesnake://agent/modes`
* `vibesnake://agent/playbook`
* `vibesnake://agent/rivals`
* `vibesnake://agent/rules`
* `vibesnake://agent/signal-school`
* `vibesnake://agent/styles`

# Live viewer

The optional same-user pipe uses `vibesnake-agent-viewer-frame-v7`. Every frame declares initial, step, burst, or finish origin and binds exact steps advanced to the pre-mutation tick and state hash. Burst frames carry closed stop reason and final-step event, while terminal truth, immutable match identity, catalog-bound Passport v4, action facts, contiguous state anchors, two ordered live style criteria, ordered lesson progress, optional replay-bound terminal style outcomes, and optional combined-evidence lesson outcomes are cross-validated before presentation. Malformed, oversized, contradictory, unknown-catalog, identity-drifting, criterion-drifting, or mixed-version input clears pending content and rejects the stream. The host keeps only the latest unsent frame, the client reports sequence gaps as coalesced earlier updates, and the packaged-host transcript exercises rejection-aware lesson recovery as well as terminal burst delivery. The verified replay remains the canonical accepted-step history, and viewer timing never advances rules or score.

# Trust boundary

The first transport is local stdio. It opens no network listener, accepts no executable, arbitrary path, action list, or custom stop predicate, and keeps opaque bearer handles in one bounded process without a separate client-authentication layer. Finalized matches are evicted first at capacity; otherwise only a live handle with no valid handle-bearing operation for 30 minutes may be reclaimed without a result or replay. Replacement construction precedes eviction, and viewer activity is never match control. The normative Agent Plugins repository labels 1.0.0 Published while the public specification website still labels it Working Draft, so Vibe Snake retains preview-quality packaging and drift review.
