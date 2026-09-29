# Jev watch

[Engineering index](README.md) | [Agent play integration](AGENT_PLAY.md) | [AI players](../design/AI_PLAYERS.md)

Status: opt-in developer preview. The ten built-in Let's Play personalities stay offline and unchanged. This tool is not an eleventh personality, not a chat model, and not part of the supported 1.0 player. `ExportRelease` still omits Agent Arena assemblies and the watch route. A visible Godot menu is separate later work because it would change reviewed screenshot pixels.

## What the client asks

Jev is TypeSafe's System One decision model. A request carries `state` plus typed `questions` and the response carries probabilities. It does not return prose or a reasoning trace. Vibe Snake asks two `choice` questions:

- `action`: one legal snake action for this step. The criteria are only the actions that are legal now.
- `intent`: one spectator label from `seek_food`, `seek_power`, `preserve_space`, `take_risk`, and `recover`. The label does not change rules, score, or collision.

Legal actions while a run is alive are `continue`, plus any turn that is neither the current heading nor its opposite. A choice is accepted only when it is one of those actions and its probability is at least `0.34`. Anything else becomes `continue` and does not queue a turn. The ticker prints both the accepted action and the model's choice, so a rejected suggestion stays visible.

The state object is the current tick, status, heading, board size, length, score, hunger, head, food, and the cells ahead, left, and right of the head. Those cells are `food`, `body`, or `empty`.

## Routes

Do not send this model to `/api/v1/chat/completions`. The pinned model id is `typesafe/jev-1.13`. The floating alias `~typesafe/jev-latest` is accepted when you pass `--model`, and it is not the default.

| Route | Request | Key variable |
| --- | --- | --- |
| `openrouter` | `POST https://openrouter.ai/api/alpha/decisions` | `OPENROUTER_API_KEY` |
| `openrouter-systemone` | `POST https://openrouter.ai/api/v1/systemone` | `OPENROUTER_API_KEY` |
| `typesafe` | `POST https://api.typesafe.ai/v1/systemone` with model `jev-1.13` | `TYPESAFE_API_KEY` |
| `local` | The absolute `http` or `https` URL passed in `--endpoint`, model `local-jev` unless `--model` is set | none |

OpenRouter and TypeSafe send `Authorization: Bearer` from the named environment variable. The key is not a command argument. Missing or blank keys fail before any request. Error text replaces the key with `[redacted]`. A local endpoint sends no authorization header. Redirects are disabled, the timeout defaults to 15 seconds and cannot exceed 60, and a response larger than 1,048,576 bytes is rejected.

A local System One-compatible server can omit answer `type`, `model`, `usage`, and `probabilities`. The client accepts that dialect. Probability falls back from `probabilities[choice]` to `confidence`, then to 0. `usage.cost` is recorded only when it is a finite number of zero or more.

## Run a watch

From the repository root, with the .NET 10.0.303 SDK:

```powershell
dotnet run --project native/tools/VibeSnake.JevPlay/VibeSnake.JevPlay.csproj -- watch --route local --endpoint http://127.0.0.1:8099/v1/systemone --seed 1 --steps 100
```

`--steps` accepts 1 through 2000. The default is 100. Exit 0 means the watch finished, exit 1 means the provider failed, and exit 2 means the command was not usable. A failed request stops the watch. The printed lines look like:

```text
tick=0 action=continue choice=up intent=seek_food probability=0.910
```

`action` is what the rules accepted. `choice` is what the model returned. `probability` is the model's reported probability for that choice, not a claim that the snake is entertaining or that the model thought about the move. When the provider reports cost, the last line is `costUsd=` with six decimal places.

Published OpenRouter pricing bills input tokens and does not bill output tokens. A 100-step watch is a small request, but it is still a live call. Tests and CI never call the network. Set the key in your own environment when you choose to spend, and do not commit it.

## What this does not change

MCP agents already play through [Agent Host](AGENT_PLAY.md). The Godot read-only view remains `./play.ps1 --agent-watch-pipe=<name> --agent-watch-token=<token>`. Jev watch is a terminal spectator over `SnakeRun`. It does not submit moves through MCP, does not write human scores, and does not enter the built-in spectator league. Network latency cannot change the score because the rules advance only after a decision returns, one clock-free step at a time.
