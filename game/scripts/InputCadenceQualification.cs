using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;
using RulesDirection = VibeSnake.Rules.Direction;

namespace VibeSnake.Game;

internal sealed record InputCadenceCaseResult(
    string DeviceClass,
    string CadenceProfile,
    int FrameCount,
    int AcceptedInputCount,
    int RejectedInputCount,
    int RulesStepCount,
    int PendingDirectionCount,
    IReadOnlyList<string> ConsumedDirections,
    string FinalStateHash);

internal sealed record InputCadenceQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    ulong Seed,
    int RulesStepMilliseconds,
    int InputCount,
    int DeviceClassCount,
    int CadenceProfileCount,
    bool PassiveStickDriftRejected,
    IReadOnlyList<string> ExpectedConsumption,
    string ExpectedFinalStateHash,
    IReadOnlyList<InputCadenceCaseResult> Cases)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// Exercises the real Godot InputMap direction bindings and the production
/// fixed-step drain with identical timed keyboard, D-pad, and stick streams.
/// The retained report proves device and presentation cadence cannot change
/// accepted input, consumption order, step count, or final rules state.
/// </summary>
internal static class InputCadenceQualification
{
    private const ulong QualificationSeed = 20260808UL;

    private static readonly TimedDirectionInput[] Inputs =
    [
        new(5.0, RulesDirection.Up),
        new(10.0, RulesDirection.Left),
        new(15.0, RulesDirection.Down),
        new(125.0, RulesDirection.Right),
        new(130.0, RulesDirection.Up),
    ];

    private static readonly DeviceDefinition[] Devices =
    [
        new("keyboard", CreateKeyboardEvent),
        new("dpad", CreateDpadEvent),
        new("stick", CreateStickEvent),
    ];

    private static readonly CadenceDefinition[] Cadences =
    [
        new("low-render-rate", [120.0, 40.0, 90.0]),
        new(
            "normal-render-rate",
            [
                16.0,
                17.0,
                17.0,
                16.0,
                17.0,
                17.0,
                16.0,
                17.0,
                17.0,
                16.0,
                17.0,
                17.0,
                16.0,
                17.0,
                17.0,
            ]),
        new("stressed-render-rate", [8.0, 112.0, 7.0, 35.0, 88.0]),
    ];

    public static InputCadenceQualificationEvidence Run()
    {
        GameActions.EnsureDefaults();
        var expectedConsumption = Inputs.Select(input => DirectionName(input.Direction)).ToArray();
        var results = new List<InputCadenceCaseResult>(Devices.Length * Cadences.Length);
        string? expectedHash = null;

        foreach (var device in Devices)
        {
            foreach (var cadence in Cadences)
            {
                var result = ExecuteCase(device, cadence);
                if (result.AcceptedInputCount != Inputs.Length
                    || result.RejectedInputCount != 0
                    || result.RulesStepCount != Inputs.Length
                    || result.PendingDirectionCount != 0
                    || !result.ConsumedDirections.SequenceEqual(expectedConsumption))
                {
                    throw new InvalidOperationException(
                        $"Input cadence case {device.Id}/{cadence.Id} changed the logical stream.");
                }

                expectedHash ??= result.FinalStateHash;
                if (!string.Equals(expectedHash, result.FinalStateHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Input cadence case {device.Id}/{cadence.Id} changed the final rules hash.");
                }

                results.Add(result);
            }
        }

        using var passiveStickMotion = new InputEventJoypadMotion
        {
            Device = 0,
            Axis = JoyAxis.LeftX,
            AxisValue = 0.2f,
        };
        var passiveStickDriftRejected = !GameActions.TryMapDirectionInput(
            passiveStickMotion,
            out _);
        if (!passiveStickDriftRejected)
        {
            throw new InvalidOperationException(
                "Passive stick drift crossed the gameplay action deadzone.");
        }

        return new InputCadenceQualificationEvidence(
            SchemaVersion: 1,
            Kind: "input-cadence-qualification-v1",
            Passed: true,
            Seed: QualificationSeed,
            RulesStepMilliseconds: RunConfig.RulesTickMilliseconds,
            InputCount: Inputs.Length,
            DeviceClassCount: Devices.Length,
            CadenceProfileCount: Cadences.Length,
            PassiveStickDriftRejected: passiveStickDriftRejected,
            ExpectedConsumption: expectedConsumption,
            ExpectedFinalStateHash: expectedHash
                ?? throw new InvalidOperationException("Input qualification produced no cases."),
            Cases: results);
    }

    private static InputCadenceCaseResult ExecuteCase(
        DeviceDefinition device,
        CadenceDefinition cadence)
    {
        var run = SnakeRun.Create(
            QualificationSeed,
            new RunConfig(
                Width: 64,
                Height: 32,
                StarvationTicks: 1_000,
                MaximumDirectionQueue: 3,
                PowerSpawnIntervalTicks: 1_000));
        var accumulatedMilliseconds = 0.0;
        var elapsedMilliseconds = 0.0;
        var inputIndex = 0;
        var acceptedInputCount = 0;
        var rejectedInputCount = 0;
        var consumedDirections = new List<string>(Inputs.Length);

        foreach (var frameMilliseconds in cadence.FrameMilliseconds)
        {
            elapsedMilliseconds += frameMilliseconds;
            while (inputIndex < Inputs.Length
                && Inputs[inputIndex].AtMilliseconds <= elapsedMilliseconds)
            {
                var input = Inputs[inputIndex++];
                using var inputEvent = device.CreateEvent(input.Direction);
                if (!GameActions.TryMapDirectionInput(inputEvent, out var mappedDirection)
                    || mappedDirection != input.Direction)
                {
                    throw new InvalidOperationException(
                        $"{device.Id} did not map {DirectionName(input.Direction)} through InputMap.");
                }

                if (run.QueueDirection(mappedDirection))
                {
                    acceptedInputCount++;
                }
                else
                {
                    rejectedInputCount++;
                }
            }

            var steps = RulesCadenceClock.DrainSteps(
                ref accumulatedMilliseconds,
                frameMilliseconds / 1000.0,
                () => run.EffectiveRulesStepMilliseconds);
            for (var stepIndex = 0; stepIndex < steps; stepIndex++)
            {
                if (run.Status != RunStatus.Running)
                {
                    throw new InvalidOperationException(
                        $"Input cadence case {device.Id}/{cadence.Id} ended before the stream completed.");
                }

                run.Step();
                consumedDirections.Add(DirectionName(run.Direction));
            }
        }

        if (inputIndex != Inputs.Length || Math.Abs(accumulatedMilliseconds) > 0.0001)
        {
            throw new InvalidOperationException(
                $"Input cadence case {device.Id}/{cadence.Id} did not drain its complete timeline.");
        }

        return new InputCadenceCaseResult(
            DeviceClass: device.Id,
            CadenceProfile: cadence.Id,
            FrameCount: cadence.FrameMilliseconds.Count,
            AcceptedInputCount: acceptedInputCount,
            RejectedInputCount: rejectedInputCount,
            RulesStepCount: consumedDirections.Count,
            PendingDirectionCount: run.PendingDirectionCount,
            ConsumedDirections: consumedDirections,
            FinalStateHash: run.ComputeStateHash());
    }

    private static InputEventKey CreateKeyboardEvent(RulesDirection direction) =>
        new InputEventKey
        {
            Pressed = true,
            Echo = false,
            PhysicalKeycode = direction switch
            {
                RulesDirection.Up => Key.Up,
                RulesDirection.Right => Key.Right,
                RulesDirection.Down => Key.Down,
                RulesDirection.Left => Key.Left,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            },
        };

    private static InputEventJoypadButton CreateDpadEvent(RulesDirection direction) =>
        new InputEventJoypadButton
        {
            Device = 0,
            Pressed = true,
            ButtonIndex = direction switch
            {
                RulesDirection.Up => JoyButton.DpadUp,
                RulesDirection.Right => JoyButton.DpadRight,
                RulesDirection.Down => JoyButton.DpadDown,
                RulesDirection.Left => JoyButton.DpadLeft,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            },
        };

    private static InputEventJoypadMotion CreateStickEvent(RulesDirection direction) =>
        new InputEventJoypadMotion
        {
            Device = 0,
            Axis = direction is RulesDirection.Up or RulesDirection.Down
                ? JoyAxis.LeftY
                : JoyAxis.LeftX,
            AxisValue = direction is RulesDirection.Up or RulesDirection.Left ? -1.0f : 1.0f,
        };

    private static string DirectionName(RulesDirection direction) => direction switch
    {
        RulesDirection.Up => "up",
        RulesDirection.Right => "right",
        RulesDirection.Down => "down",
        RulesDirection.Left => "left",
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private readonly record struct TimedDirectionInput(
        double AtMilliseconds,
        RulesDirection Direction);

    private sealed record DeviceDefinition(
        string Id,
        Func<RulesDirection, InputEvent> CreateEvent);

    private sealed record CadenceDefinition(
        string Id,
        IReadOnlyList<double> FrameMilliseconds);
}
