# AgentHost 0.7.0 Playtest Reproduction

## Bug 1: finish_match lifecycle=aborted on successful lessons

### Reproduction

1. Start lesson `first-turn` with `four-direction-step-v1` profile, seed 7
2. Play move `left` (opposite direction) - should be rejected as `illegal_direction`
3. Play move `up` (legal direction) - should complete lesson with `target_reached`
4. Call `finish_match`
5. Observe `lesson_outcome.review_code` is `target_reached` and `all_requirements_satisfied` is `true`
6. **Bug**: `lifecycle` was `aborted` instead of `completed`

### Expected behavior

When `all_requirements_satisfied` is true and `review_code` is `target_reached`, the `lifecycle` should be `completed` to indicate successful lesson completion, not `aborted` which suggests failure.

### Fix

Added `DetermineFinishLifecycle()` method in `AgentMatchSession.cs` that returns:
- `Completed` when lesson has all requirements satisfied
- `Completed` when run status is terminal (not Running)
- `Completed` when step limit is reached
- `Aborted` otherwise (intentional early exit)

### Verification

Run the first-turn lesson scenario:
```
start_lesson first-turn four-direction-step-v1
play_move left -> illegal_direction rejection
play_move up -> target_reached
finish_match -> lifecycle=completed (not aborted)
```

## Bug 2: play_burst parameter error messages

### Reproduction

1. Start any match with `four-direction-burst-v1` profile
2. Call `play_burst` with parameter `action=continue` (wrong parameter name)
3. **Bug**: Error message is generic "An error occurred invoking 'play_burst'." with no field name

### Expected behavior

Error should indicate which parameter is wrong: "Missing required parameter 'initialAction'" or similar.

### Root cause

MCP SDK parameter binding fails before application code is reached when JSON has wrong parameter names. The SDK error message is generic and does not specify which parameter is expected.

### Partial fix

Added `ArgumentNullException` and `ArgumentOutOfRangeException` to the exception handling in `McpAgentTools.Execute()`. However, the core issue is SDK-level parameter binding that occurs before application code executes.

### Limitation

This is an MCP SDK limitation. The application cannot improve error messages for parameter binding failures that occur in the SDK before the application method is invoked.

## Bug 3: style_outcome finish reads as failed grade

### Reproduction

1. Start match `stillwater` classic open seed 10447140706510876853
2. Play 22 steps, eat 1 food, score 10
3. Call `finish_match`
4. **Issue**: `style_outcome` shows `survival_steps 22/200 unsatisfied`, which reads like a failure even though the agent deliberately ended early after achieving their goal

### Expected behavior

Style criteria should be presented as measurements/facts rather than pass/fail grades, especially when agent intentionally ends early.

### Fix

Updated `finish_match` tool description to clarify:
- "Style criteria show measurements, not grades"
- When `lifecycle` is `completed` vs `aborted`
- That `aborted` indicates intentional early exit, not failure

### Note

The style outcome data structure is correct. The issue is about interpretation: criteria marked "unsatisfied" are measurements of what was achieved, not judgments of failure. Documentation and description improvements address this without requiring breaking changes to the data structure.
