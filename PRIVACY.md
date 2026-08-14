# Privacy

Status: release-material foundation for the Vibe Snake alpha. Final candidate review is pending.

Vibe Snake is designed to work offline. The native game has no account, telemetry, analytics, advertising, cloud-save, matchmaking, or automatic upload service. Playing the game does not require a network connection.

## Data stored on the device

The game may store settings, input bindings, onboarding state, achievements, progression, local scores, replays, offline household comparisons, optional-content state, recovery backups, local logs, and local diagnostics in the operating system's application-data location. The exact native layout and platform roots are documented in the [user-data contract](docs/engineering/USER_DATA.md).

Local playtest summaries are disabled by default. If enabled, they contain a closed set of aggregate run facts and no player name, account, raw input, raw timing, system path, device serial, IP address, or free text. Export is an explicit local action. The game has no summary uploader.

Logs and diagnostics remain local. Production boundaries sanitize private path prefixes, cap file sizes, and exclude credentials, raw input, and unrelated device information. A player chooses whether to share a reviewed file outside the game.

## Network and optional content

Core play remains offline. Optional content is installed from explicit local packages and validated before use. The game does not download or update a content pack by itself. Any future storefront may perform delivery or updates under that storefront's separate terms, but it must not become a gameplay service or receive game telemetry from Vibe Snake.

## Agent Arena developer preview

The optional post-1.0 Agent Arena source uses a local stdio MCP host and opens no network listener. Agent matches are ephemeral by default and receive only a closed public logical-state projection. That projection includes exact pending directions and public rules timers needed for deterministic symbolic control, so it is not identical to the human presentation. It excludes random state, future outcomes, controller internals, private paths, and human data. Agents cannot read or update human profiles, settings, achievements, progression, scores, household comparisons, or the built-in spectator league.

An optional public Agent Passport may contain a bounded agent ID, policy version, display name, color, shed, station affinity, and fixed capability profile. It must not contain prompts, hidden reasoning, credentials, provider responses, account data, or other personal information. The host does not persist the passport as agent memory.

Each step or bounded burst may carry one closed public intent label: seek food, seek power, preserve space, take risk, recover, or undeclared. It contains no free-form text, is visible to the agent and local viewer, has no gameplay authority, and is not written into human profiles or progression. A burst accepts no action list, custom stop expression, executable code, or hidden state and advances at most 16 clock-free rules steps before returning public final-step evidence.

Signal School uses only a published lesson ID and canonical public practice configuration. Its instruction, primary-metric progress, mutation delta, and replay-bound outcome contain no provider data or human profile data. Practice history is not persisted by this preview.

The host retains at most eight ephemeral matches. At capacity it removes finalized matches first; if every match is live, it may invalidate only a handle with no valid handle-bearing host operation for at least 30 minutes. The opaque handle is the bearer capability; the stdio host has no separate client-authentication layer. This resource lease creates no score, result, ranking, replay, or analytics record. Viewer connection and disconnection never refresh, finish, or expire a match.

Live watching uses a one-time same-user local-pipe capability. The capability is not written to the replay or application logs and should not be copied into screenshots, reports, or shared command history. A verified replay is stored only after the agent explicitly calls the path-free save tool, and it uses the same bounded application-owned replay store and deletion controls as other local replays.

This preview provides no upload, matchmaking, remote transport, hosted tournament, model-provider integration, or agent analytics. Any such feature requires a separate architecture and privacy review before it is enabled.

## External testing and reports

Controlled external validation uses pseudonymous participant IDs. Consent records stay separate from session observations and outside the public repository. Retained reports must be reviewed and de-identified. Names, accounts, contact details, private paths, device serials, raw input, raw timing, and unrelated device data are forbidden from the validation record.

Public support intake is currently closed while the project is an alpha. Before a public release, [SUPPORT.md](SUPPORT.md) must name a tested route. Sending a report through that future route is voluntary. Players should remove personal information before attaching logs, screenshots, saves, or diagnostics.

## Control and deletion

The Data settings screen separates preferences, progression, local scores, replays, and optional content. Confirmed reset creates a bounded local backup before removing only the selected application-owned category. Local playtest summaries use a separate permanent-deletion action. Player-supplied import sources are not reset targets.

Removing the application does not remove external player data. A player who wants complete local removal must first use the documented reset and deletion controls, then remove the remaining platform user-data directory manually. See the [recovery guide](docs/guides/RECOVERY.md) before deleting files.

## Release boundary

This statement describes the implemented offline architecture. A final release must recheck the exact packaged artifact, storefront wrapper, support route, save locations, and optional-content delivery. Any future network feature requires a reviewed privacy update before it is enabled.
