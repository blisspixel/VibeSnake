using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class AudioMixAllocatorTests
{
    [Fact]
    public void Grants_voices_expires_them_and_reports_strongest_duck()
    {
        var allocator = CreateAllocator();

        var first = allocator.Request(Request("food", "SFX", 40, 0, duck: -2.0f));
        var second = allocator.Request(Request("warning", "SFX", 80, 1, duck: -6.0f));

        Assert.True(first.IsGranted);
        Assert.Equal(AudioMixDecisionCode.Granted, first.Code);
        Assert.NotNull(first.Lease);
        Assert.Empty(first.Interrupted);
        Assert.True(second.IsGranted);
        Assert.Equal(2, allocator.ActiveVoiceCount);
        Assert.Equal(-6.0f, allocator.EffectiveMusicDuckDecibels);
        Assert.Equal([1L, 2L], allocator.ActiveLeases.Select(lease => lease.LeaseId));

        var advanced = allocator.Advance(101);

        Assert.Equal([1L, 2L], advanced.ExpiredLeaseIds);
        Assert.Equal(0.0f, advanced.EffectiveMusicDuckDecibels);
        Assert.Equal(0, allocator.ActiveVoiceCount);
    }

    [Fact]
    public void Cooldown_suppresses_before_boundary_and_only_grants_update_it()
    {
        var allocator = CreateAllocator();
        var first = allocator.Request(Request("confirm", "UI", 50, 10, cooldown: 50));
        var suppressed = allocator.Request(Request("confirm", "UI", 50, 59, cooldown: 50));
        var boundary = allocator.Request(Request("confirm", "UI", 50, 60, cooldown: 50));

        Assert.True(first.IsGranted);
        Assert.Equal(AudioMixDecisionCode.SuppressedByCooldown, suppressed.Code);
        Assert.False(suppressed.IsGranted);
        Assert.Null(suppressed.Lease);
        Assert.True(boundary.IsGranted);
    }

    [Fact]
    public void Shared_cooldown_groups_coalesce_distinct_cues()
    {
        var allocator = CreateAllocator();

        var first = allocator.Request(Request(
            "power-a",
            "SFX",
            50,
            0,
            cooldown: 50,
            group: "power"));
        var second = allocator.Request(Request(
            "power-b",
            "SFX",
            50,
            20,
            cooldown: 50,
            group: "power"));
        var independent = allocator.Request(Request(
            "ui",
            "UI",
            50,
            20,
            cooldown: 50,
            group: "ui"));

        Assert.True(first.IsGranted);
        Assert.Equal(AudioMixDecisionCode.SuppressedByCooldown, second.Code);
        Assert.True(independent.IsGranted);
    }

    [Fact]
    public void Polyphony_suppresses_equal_priority_and_interrupts_lower_priority()
    {
        var allocator = CreateAllocator();
        var low = allocator.Request(Request("food", "SFX", 20, 0, polyphony: 1));
        var equal = allocator.Request(Request("food", "SFX", 20, 1, polyphony: 1));
        var high = allocator.Request(Request(
            "food",
            "SFX",
            60,
            2,
            polyphony: 1,
            mayInterrupt: true));

        Assert.True(low.IsGranted);
        Assert.Equal(AudioMixDecisionCode.SuppressedByPolyphony, equal.Code);
        Assert.Equal(AudioMixDecisionCode.GrantedWithInterruption, high.Code);
        Assert.Equal([low.Lease!.LeaseId], high.Interrupted);
        Assert.Equal([high.Lease!.LeaseId], allocator.ActiveLeases.Select(lease => lease.LeaseId));
    }

    [Fact]
    public void Full_bus_suppresses_lower_priority_and_allows_higher_interruption()
    {
        var allocator = new AudioMixAllocator(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["SFX"] = 1 });
        var existing = allocator.Request(Request("existing", "SFX", 50, 0));
        var lower = allocator.Request(Request(
            "lower",
            "SFX",
            40,
            1,
            mayInterrupt: true));
        var higher = allocator.Request(Request(
            "higher",
            "SFX",
            90,
            2,
            mayInterrupt: true));

        Assert.True(existing.IsGranted);
        Assert.Equal(AudioMixDecisionCode.SuppressedByPriority, lower.Code);
        Assert.Equal(AudioMixDecisionCode.GrantedWithInterruption, higher.Code);
        Assert.Equal([existing.Lease!.LeaseId], higher.Interrupted);
    }

    [Fact]
    public void Bus_capacities_are_independent_and_victim_order_is_stable()
    {
        var allocator = CreateAllocator();
        var older = allocator.Request(Request("older", "SFX", 10, 0));
        var newer = allocator.Request(Request("newer", "SFX", 10, 1));
        var ui = allocator.Request(Request("ui", "UI", 10, 1));
        var interrupt = allocator.Request(Request(
            "critical",
            "SFX",
            100,
            2,
            mayInterrupt: true));

        Assert.True(older.IsGranted);
        Assert.True(newer.IsGranted);
        Assert.True(ui.IsGranted);
        Assert.Equal(AudioMixDecisionCode.GrantedWithInterruption, interrupt.Code);
        Assert.Equal([older.Lease!.LeaseId], interrupt.Interrupted);
        Assert.Contains(allocator.ActiveLeases, lease => lease.LeaseId == ui.Lease!.LeaseId);
    }

    [Fact]
    public void Release_and_reset_are_idempotent_and_restore_initial_identity()
    {
        var allocator = CreateAllocator();
        var lease = allocator.Request(Request("food", "SFX", 40, 0)).Lease!;

        Assert.True(allocator.Release(lease.LeaseId));
        Assert.False(allocator.Release(lease.LeaseId));
        Assert.Equal(0.0f, allocator.EffectiveMusicDuckDecibels);

        allocator.Request(Request("ui", "UI", 50, 1));
        allocator.Reset();
        Assert.Empty(allocator.ActiveLeases);
        var afterReset = allocator.Request(Request("fresh", "SFX", 50, 0));
        Assert.Equal(1L, afterReset.Lease!.LeaseId);
    }

    [Theory]
    [InlineData("cue-empty")]
    [InlineData("bus-empty")]
    [InlineData("bus-unknown")]
    [InlineData("priority-low")]
    [InlineData("priority-high")]
    [InlineData("time-negative")]
    [InlineData("cooldown-negative")]
    [InlineData("cooldown-high")]
    [InlineData("polyphony-zero")]
    [InlineData("polyphony-high")]
    [InlineData("duration-zero")]
    [InlineData("duration-high")]
    [InlineData("duck-low")]
    [InlineData("duck-high")]
    [InlineData("group-empty")]
    public void Invalid_requests_fail_without_mutating_allocator(string mutation)
    {
        var allocator = CreateAllocator();
        var request = Request("cue", "SFX", 50, 0);
        request = mutation switch
        {
            "cue-empty" => request with { CueId = "" },
            "bus-empty" => request with { Bus = "" },
            "bus-unknown" => request with { Bus = "Unknown" },
            "priority-low" => request with { Priority = -1 },
            "priority-high" => request with { Priority = 101 },
            "time-negative" => request with { RequestedAtMilliseconds = -1 },
            "cooldown-negative" => request with { CooldownMilliseconds = -1 },
            "cooldown-high" => request with
            {
                CooldownMilliseconds = AudioMixAllocator.MaximumCooldownMilliseconds + 1,
            },
            "polyphony-zero" => request with { MaximumPolyphony = 0 },
            "polyphony-high" => request with
            {
                MaximumPolyphony = AudioMixAllocator.MaximumSupportedBusVoices + 1,
            },
            "duration-zero" => request with { ExpectedDurationMilliseconds = 0 },
            "duration-high" => request with
            {
                ExpectedDurationMilliseconds =
                    AudioMixAllocator.MaximumExpectedDurationMilliseconds + 1,
            },
            "duck-low" => request with
            {
                MusicDuckDecibels = AudioMixAllocator.MinimumMusicDuckDecibels - 0.1f,
            },
            "duck-high" => request with { MusicDuckDecibels = 0.1f },
            "group-empty" => request with { CooldownGroup = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        var result = allocator.Request(request);

        Assert.Equal(AudioMixDecisionCode.InvalidRequest, result.Code);
        Assert.False(result.IsGranted);
        Assert.Empty(result.Interrupted);
        Assert.Equal(0, allocator.ActiveVoiceCount);
    }

    [Fact]
    public void Nonmonotonic_time_is_rejected_by_requests_and_advance()
    {
        var allocator = CreateAllocator();
        Assert.True(allocator.Request(Request("first", "SFX", 50, 10)).IsGranted);

        var backward = allocator.Request(Request("backward", "SFX", 50, 9));

        Assert.Equal(AudioMixDecisionCode.InvalidRequest, backward.Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Advance(9));
    }

    [Fact]
    public void Constructor_rejects_missing_invalid_or_excessive_bus_capacity()
    {
        Assert.Throws<ArgumentNullException>(() => new AudioMixAllocator(null!));
        Assert.Throws<ArgumentException>(() =>
            new AudioMixAllocator(new Dictionary<string, int>()));
        Assert.Throws<ArgumentException>(() =>
            new AudioMixAllocator(new Dictionary<string, int> { [""] = 1 }));
        Assert.Throws<ArgumentException>(() =>
            new AudioMixAllocator(new Dictionary<string, int> { ["SFX"] = 0 }));
        Assert.Throws<ArgumentException>(() =>
            new AudioMixAllocator(new Dictionary<string, int>
            {
                ["SFX"] = AudioMixAllocator.MaximumSupportedBusVoices + 1,
            }));
        Assert.Throws<ArgumentException>(() =>
            new AudioMixAllocator(Enumerable.Range(0, AudioMixAllocator.MaximumBusCount + 1)
                .ToDictionary(index => $"bus-{index}", _ => 1)));
        Assert.Throws<ArgumentException>(() =>
            new AudioMixAllocator(new Dictionary<string, int>
            {
                [new string('b', AudioMixAllocator.MaximumIdentifierCharacters + 1)] = 1,
            }));
    }

    [Fact]
    public void Identifier_expiry_overflow_and_group_capacity_are_rejected()
    {
        var allocator = CreateAllocator();
        var tooLong = new string('c', AudioMixAllocator.MaximumIdentifierCharacters + 1);

        Assert.Equal(
            AudioMixDecisionCode.InvalidRequest,
            allocator.Request(Request(tooLong, "SFX", 50, 0)).Code);
        Assert.Equal(
            AudioMixDecisionCode.InvalidRequest,
            allocator.Request(Request("cue", "SFX", 50, 0) with
            {
                CooldownGroup = tooLong,
            }).Code);
        Assert.Equal(
            AudioMixDecisionCode.InvalidRequest,
            allocator.Request(Request("cue", "SFX", 50, long.MaxValue) with
            {
                ExpectedDurationMilliseconds = 1,
            }).Code);

        for (var index = 0; index < AudioMixAllocator.MaximumCooldownGroupCount; index++)
        {
            var decision = allocator.Request(Request(
                $"cue-{index}",
                "SFX",
                50,
                index * 100L,
                group: $"group-{index}"));
            Assert.True(decision.IsGranted);
        }

        var overflow = allocator.Request(Request(
            "overflow",
            "SFX",
            50,
            AudioMixAllocator.MaximumCooldownGroupCount * 100L,
            group: "overflow-group"));
        Assert.Equal(AudioMixDecisionCode.InvalidRequest, overflow.Code);
    }

    private static AudioMixAllocator CreateAllocator() => new(
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["SFX"] = 2,
            ["UI"] = 2,
        });

    private static AudioMixRequest Request(
        string cue,
        string bus,
        int priority,
        long at,
        int cooldown = 0,
        int polyphony = 4,
        bool mayInterrupt = false,
        float duck = 0.0f,
        string? group = null) => new(
            CueId: cue,
            Bus: bus,
            Priority: priority,
            RequestedAtMilliseconds: at,
            CooldownMilliseconds: cooldown,
            MaximumPolyphony: polyphony,
            ExpectedDurationMilliseconds: 100,
            MayInterruptLowerPriority: mayInterrupt,
            MusicDuckDecibels: duck,
            CooldownGroup: group);
}
