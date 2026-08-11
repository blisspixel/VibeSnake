using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using VibeSnake.Persistence;

namespace VibeSnake.Game;

internal enum AudioCue
{
    Navigate,
    Confirm,
    Back,
    Pause,
    Restart,
    Achievement,
    Food,
    ComboTier1,
    ComboTier2,
    ComboTier3,
    ComboTier4,
    ComboBreak,
    Starvation,
    Collision,
    StarvationDeath,
    ShieldSpawn,
    ShieldActivate,
    PhaseShiftActivate,
    LastStandActivate,
    SlowMoActivate,
    BoostActivate,
    MagnetActivate,
    BaitActivate,
    GluttonyActivate,
    SegmentDetachActivate,
    ShieldExpire,
    ShieldBreak,
    PowerSpawn,
    PowerExpire,
    PowerRecovery,
    Victory,
}

internal static class AudioBuses
{
    public const string Master = "Master";
    public const string Music = "Music";
    public const string Sfx = "SFX";
    public const string Ui = "UI";

    private const string MonoDownmixEffectName = "VibeSnake Mono Downmix";
    private static float _musicVolumeLinear = 0.8f;
    private static bool _musicMuted;
    private static float _transientMusicDuckDecibels;

    public static void EnsureRegistered()
    {
        EnsureBus(Music);
        EnsureBus(Sfx);
        EnsureBus(Ui);
        EnsureMonoDownmixEffect();
    }

    public static void AssertRegistered()
    {
        foreach (var bus in new[] { Music, Sfx, Ui })
        {
            if (AudioServer.GetBusIndex(bus) < 0)
            {
                throw new InvalidOperationException(
                    $"Required audio bus is not registered: {bus}");
            }
        }

        var monoEffectIndex = FindMonoDownmixEffectIndex();
        var masterIndex = AudioServer.GetBusIndex(Master);
        if (monoEffectIndex < 0
            || AudioServer.GetBusEffect(masterIndex, monoEffectIndex)
                is not AudioEffectStereoEnhance stereo
            || Math.Abs(stereo.PanPullout) >= 0.0001f)
        {
            throw new InvalidOperationException(
                "Required Master-bus mono downmix effect is not configured.");
        }
    }

    /// <summary>
    /// Applies multi-bus linear volumes and mute flags from shell settings.
    /// Master is the engine Master bus; Music/SFX/UI keep relative gains.
    /// </summary>
    public static void ApplyShellSettings(ShellSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureRegistered();
        settings.Clamp();

        SetBusLinear(Master, settings.MasterMuted ? 0.0f : settings.MasterVolume);
        _musicVolumeLinear = settings.MusicVolume;
        _musicMuted = settings.MusicMuted;
        ApplyMusicBus();
        SetBusLinear(Sfx, settings.SfxMuted ? 0.0f : settings.SfxVolume);
        SetBusLinear(Ui, settings.UiMuted ? 0.0f : settings.UiVolume);

        var masterIndex = AudioServer.GetBusIndex(Master);
        var monoEffectIndex = EnsureMonoDownmixEffect();
        AudioServer.SetBusEffectEnabled(masterIndex, monoEffectIndex, settings.MonoOutput);
    }

    public static bool IsMonoOutputApplied()
    {
        var masterIndex = AudioServer.GetBusIndex(Master);
        var effectIndex = FindMonoDownmixEffectIndex();
        return masterIndex >= 0
            && effectIndex >= 0
            && AudioServer.GetBusEffect(masterIndex, effectIndex)
                is AudioEffectStereoEnhance stereo
            && Math.Abs(stereo.PanPullout) < 0.0001f
            && AudioServer.IsBusEffectEnabled(masterIndex, effectIndex);
    }

    public static int MonoDownmixEffectCount()
    {
        var masterIndex = AudioServer.GetBusIndex(Master);
        if (masterIndex < 0)
        {
            return 0;
        }

        var count = 0;
        for (var index = 0; index < AudioServer.GetBusEffectCount(masterIndex); index++)
        {
            if (IsOwnedMonoEffect(AudioServer.GetBusEffect(masterIndex, index)))
            {
                count++;
            }
        }

        return count;
    }

    public static float GetBusLinear(string busName)
    {
        var index = AudioServer.GetBusIndex(busName);
        if (index < 0)
        {
            throw new InvalidOperationException("Unknown audio bus: " + busName);
        }

        if (AudioServer.IsBusMute(index))
        {
            return 0.0f;
        }

        return Mathf.DbToLinear(AudioServer.GetBusVolumeDb(index));
    }

    public static float TransientMusicDuckDecibels => _transientMusicDuckDecibels;

    public static void SetTransientMusicDuck(float decibels)
    {
        if (float.IsNaN(decibels) || float.IsInfinity(decibels))
        {
            throw new ArgumentOutOfRangeException(nameof(decibels));
        }

        _transientMusicDuckDecibels = Math.Clamp(
            decibels,
            AudioMixAllocator.MinimumMusicDuckDecibels,
            0.0f);
        ApplyMusicBus();
    }

    private static void ApplyMusicBus()
    {
        var duckScale = Mathf.DbToLinear(_transientMusicDuckDecibels);
        SetBusLinear(
            Music,
            _musicMuted ? 0.0f : _musicVolumeLinear * duckScale);
    }

    private static void SetBusLinear(string busName, float linear)
    {
        var index = AudioServer.GetBusIndex(busName);
        if (index < 0)
        {
            throw new InvalidOperationException("Unknown audio bus: " + busName);
        }

        var clamped = Math.Clamp(linear, 0.0f, 1.0f);
        if (clamped <= 0.0001f)
        {
            AudioServer.SetBusMute(index, true);
            AudioServer.SetBusVolumeDb(index, -80.0f);
            return;
        }

        AudioServer.SetBusMute(index, false);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(clamped));
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
        {
            return;
        }

        var index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, Master);
    }

    private static int EnsureMonoDownmixEffect()
    {
        var existingIndex = FindMonoDownmixEffectIndex();
        if (existingIndex >= 0)
        {
            var masterIndex = AudioServer.GetBusIndex(Master);
            var effect = (AudioEffectStereoEnhance)AudioServer.GetBusEffect(
                masterIndex,
                existingIndex);
            effect.PanPullout = 0.0f;
            return existingIndex;
        }

        var index = AudioServer.GetBusIndex(Master);
        if (index < 0)
        {
            throw new InvalidOperationException("Master audio bus is unavailable.");
        }

        var downmix = new AudioEffectStereoEnhance
        {
            ResourceName = MonoDownmixEffectName,
            PanPullout = 0.0f,
        };
        AudioServer.AddBusEffect(index, downmix);
        return AudioServer.GetBusEffectCount(index) - 1;
    }

    private static int FindMonoDownmixEffectIndex()
    {
        var masterIndex = AudioServer.GetBusIndex(Master);
        if (masterIndex < 0)
        {
            return -1;
        }

        for (var index = 0; index < AudioServer.GetBusEffectCount(masterIndex); index++)
        {
            if (IsOwnedMonoEffect(AudioServer.GetBusEffect(masterIndex, index)))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsOwnedMonoEffect(AudioEffect effect) =>
        effect is AudioEffectStereoEnhance stereo
        && string.Equals(stereo.ResourceName, MonoDownmixEffectName, StringComparison.Ordinal);
}

internal sealed record AudioCueMixPolicyEntry(
    AudioCue Cue,
    string Bus,
    int Priority,
    int CooldownMilliseconds,
    int MaximumPolyphony,
    int ExpectedDurationMilliseconds,
    bool MayInterruptLowerPriority,
    float MusicDuckDecibels,
    string CooldownGroup)
{
    public AudioMixRequest ToRequest(long requestedAtMilliseconds) => new(
        CueId: Cue.ToString(),
        Bus: Bus,
        Priority: Priority,
        RequestedAtMilliseconds: requestedAtMilliseconds,
        CooldownMilliseconds: CooldownMilliseconds,
        MaximumPolyphony: MaximumPolyphony,
        ExpectedDurationMilliseconds: ExpectedDurationMilliseconds,
        MayInterruptLowerPriority: MayInterruptLowerPriority,
        MusicDuckDecibels: MusicDuckDecibels,
        CooldownGroup: CooldownGroup);
}

internal sealed record AudioMixPolicyQualification(
    int CueCount,
    bool CatalogComplete,
    bool BusRoutingObserved,
    bool CooldownSuppressionObserved,
    bool PolyphonySuppressionObserved,
    bool PrioritySuppressionObserved,
    bool InterruptionObserved,
    bool DuckObserved,
    bool DuckRestorationObserved,
    bool BusIsolationObserved,
    bool UnitTestableWithoutPlayback)
{
    public bool Passed => CatalogComplete
        && BusRoutingObserved
        && CooldownSuppressionObserved
        && PolyphonySuppressionObserved
        && PrioritySuppressionObserved
        && InterruptionObserved
        && DuckObserved
        && DuckRestorationObserved
        && BusIsolationObserved
        && UnitTestableWithoutPlayback;
}

internal sealed record ProceduralCueMeasurement(
    AudioCue Cue,
    string Bus,
    int DurationMilliseconds,
    int MixRate,
    int ChannelCount,
    float PeakDecibelsFullScale,
    string PcmSha256);

/// <summary>
/// Closed allocation policy for every current runtime cue. The feedback matrix
/// remains the event-level authority; this is the fallback player's bounded
/// bus, concurrency, cooldown, interruption, and ducking contract.
/// </summary>
internal static class AudioCueMixPolicy
{
    public const int SfxBusCapacity = 8;
    public const int UiBusCapacity = 4;

    public static IReadOnlyDictionary<string, int> BusCapacities { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [AudioBuses.Sfx] = SfxBusCapacity,
            [AudioBuses.Ui] = UiBusCapacity,
        };

    public static IReadOnlyList<AudioCueMixPolicyEntry> Entries { get; } =
        Enum.GetValues<AudioCue>().Select(For).ToArray();

    public static AudioCueMixPolicyEntry For(AudioCue cue) => cue switch
    {
        AudioCue.Navigate => Entry(cue, AudioBuses.Ui, 35, 25, 2, 50, group: "ui-navigate"),
        AudioCue.Confirm => Entry(cue, AudioBuses.Ui, 50, 35, 2, 70, group: "ui-confirm"),
        AudioCue.Back => Entry(cue, AudioBuses.Ui, 50, 35, 2, 70, group: "ui-back"),
        AudioCue.Pause => Entry(cue, AudioBuses.Ui, 80, 100, 1, 80, group: "ui-pause"),
        AudioCue.Restart => Entry(cue, AudioBuses.Ui, 85, 250, 1, 110, group: "ui-restart"),
        AudioCue.Achievement => Entry(cue, AudioBuses.Ui, 92, 500, 1, 240, duck: -3.0f, group: "achievement"),
        AudioCue.Food => Entry(cue, AudioBuses.Sfx, 40, 35, 3, 90, group: "food"),
        AudioCue.ComboTier1 => Entry(cue, AudioBuses.Sfx, 40, 250, 1, 100, group: "combo-tier-1"),
        AudioCue.ComboTier2 => Entry(cue, AudioBuses.Sfx, 40, 250, 1, 120, group: "combo-tier-2"),
        AudioCue.ComboTier3 => Entry(cue, AudioBuses.Sfx, 40, 250, 1, 150, group: "combo-tier-3"),
        AudioCue.ComboTier4 => Entry(cue, AudioBuses.Sfx, 40, 500, 1, 180, group: "combo-tier-4"),
        AudioCue.ComboBreak => Entry(cue, AudioBuses.Sfx, 45, 250, 1, 140, group: "combo-break"),
        AudioCue.Starvation => Entry(cue, AudioBuses.Sfx, 55, 1_000, 1, 200, true, -5.0f, "starvation"),
        AudioCue.Collision => Entry(cue, AudioBuses.Sfx, 100, 500, 1, 180, true, -9.0f, "death"),
        AudioCue.StarvationDeath => Entry(cue, AudioBuses.Sfx, 100, 500, 1, 220, true, -9.0f, "death"),
        AudioCue.ShieldSpawn => Entry(cue, AudioBuses.Sfx, 60, 100, 2, 140, group: "power-spawn"),
        AudioCue.ShieldActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 180, duck: -3.0f, group: "activate-shield"),
        AudioCue.PhaseShiftActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 170, duck: -3.0f, group: "activate-phase-shift"),
        AudioCue.LastStandActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 210, duck: -3.0f, group: "activate-last-stand"),
        AudioCue.SlowMoActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 190, duck: -3.0f, group: "activate-slow-mo"),
        AudioCue.BoostActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 130, duck: -3.0f, group: "activate-boost"),
        AudioCue.MagnetActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 160, duck: -3.0f, group: "activate-magnet"),
        AudioCue.BaitActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 120, duck: -3.0f, group: "activate-bait"),
        AudioCue.GluttonyActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 150, duck: -3.0f, group: "activate-gluttony"),
        AudioCue.SegmentDetachActivate => Entry(cue, AudioBuses.Sfx, 80, 100, 2, 200, duck: -3.0f, group: "activate-segment-detach"),
        AudioCue.ShieldExpire => Entry(cue, AudioBuses.Sfx, 70, 100, 2, 160, group: "power-expire"),
        AudioCue.ShieldBreak => Entry(cue, AudioBuses.Sfx, 90, 250, 1, 220, true, -6.0f, "power-recovery"),
        AudioCue.PowerSpawn => Entry(cue, AudioBuses.Sfx, 60, 100, 2, 130, group: "power-spawn"),
        AudioCue.PowerExpire => Entry(cue, AudioBuses.Sfx, 70, 100, 2, 140, group: "power-expire"),
        AudioCue.PowerRecovery => Entry(cue, AudioBuses.Sfx, 90, 250, 1, 220, true, -6.0f, "power-recovery"),
        AudioCue.Victory => Entry(cue, AudioBuses.Sfx, 95, 1_000, 1, 240, true, -6.0f, "victory"),
        _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, "Unknown audio cue."),
    };

    public static AudioMixPolicyQualification Qualify()
    {
        var cues = Enum.GetValues<AudioCue>();
        var entries = Entries;
        var catalogComplete = entries.Count == cues.Length
            && entries.Select(entry => entry.Cue).SequenceEqual(cues)
            && entries.Select(entry => entry.Cue).Distinct().Count() == cues.Length
            && entries.All(entry =>
                entry.Priority is >= 0 and <= 100
                && entry.CooldownMilliseconds is >= 0
                    and <= AudioMixAllocator.MaximumCooldownMilliseconds
                && entry.MaximumPolyphony is >= 1
                    and <= AudioMixAllocator.MaximumSupportedBusVoices
                && entry.ExpectedDurationMilliseconds is >= 1
                    and <= AudioMixAllocator.MaximumExpectedDurationMilliseconds
                && entry.MusicDuckDecibels is >= AudioMixAllocator.MinimumMusicDuckDecibels
                    and <= 0.0f
                && !string.IsNullOrWhiteSpace(entry.CooldownGroup));
        const int expectedUiCueCount = 6;
        var busRoutingObserved = entries.Count(entry => entry.Bus == AudioBuses.Ui)
                == expectedUiCueCount
            && entries.Count(entry => entry.Bus == AudioBuses.Sfx)
                == cues.Length - expectedUiCueCount;

        var cooldownAllocator = new AudioMixAllocator(BusCapacities);
        var confirm = For(AudioCue.Confirm);
        var firstConfirm = cooldownAllocator.Request(confirm.ToRequest(0));
        var repeatedConfirm = cooldownAllocator.Request(confirm.ToRequest(1));
        var cooldownSuppressionObserved = firstConfirm.IsGranted
            && repeatedConfirm.Code == AudioMixDecisionCode.SuppressedByCooldown;

        var polyphonyAllocator = new AudioMixAllocator(BusCapacities);
        var polyphonyRequest = For(AudioCue.Food).ToRequest(0) with
        {
            CooldownMilliseconds = 0,
            MaximumPolyphony = 1,
            ExpectedDurationMilliseconds = 100,
        };
        var firstFood = polyphonyAllocator.Request(polyphonyRequest);
        var repeatedFood = polyphonyAllocator.Request(polyphonyRequest with
        {
            RequestedAtMilliseconds = 1,
        });
        var polyphonySuppressionObserved = firstFood.IsGranted
            && repeatedFood.Code == AudioMixDecisionCode.SuppressedByPolyphony;

        var priorityAllocator = new AudioMixAllocator(
            new Dictionary<string, int>(StringComparer.Ordinal) { [AudioBuses.Sfx] = 1 });
        var existing = priorityAllocator.Request(For(AudioCue.Food).ToRequest(0) with
        {
            CooldownMilliseconds = 0,
            ExpectedDurationMilliseconds = 100,
        });
        var lower = priorityAllocator.Request(For(AudioCue.Food).ToRequest(1) with
        {
            CueId = "lower-priority",
            Priority = 20,
            CooldownMilliseconds = 0,
            CooldownGroup = "lower-priority",
            MayInterruptLowerPriority = true,
            ExpectedDurationMilliseconds = 100,
        });
        var collision = priorityAllocator.Request(For(AudioCue.Collision).ToRequest(2) with
        {
            CooldownMilliseconds = 0,
        });
        var duckObserved = Math.Abs(
            priorityAllocator.EffectiveMusicDuckDecibels - (-9.0f)) < 0.0001f;
        var afterExpiry = priorityAllocator.Advance(182);

        var isolationAllocator = new AudioMixAllocator(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [AudioBuses.Sfx] = 1,
                [AudioBuses.Ui] = 1,
            });
        var uiVoice = isolationAllocator.Request(confirm.ToRequest(0));
        var sfxVoice = isolationAllocator.Request(For(AudioCue.Food).ToRequest(0));

        var result = new AudioMixPolicyQualification(
            CueCount: cues.Length,
            CatalogComplete: catalogComplete,
            BusRoutingObserved: busRoutingObserved,
            CooldownSuppressionObserved: cooldownSuppressionObserved,
            PolyphonySuppressionObserved: polyphonySuppressionObserved,
            PrioritySuppressionObserved: existing.IsGranted
                && lower.Code == AudioMixDecisionCode.SuppressedByPriority,
            InterruptionObserved: collision.Code == AudioMixDecisionCode.GrantedWithInterruption
                && collision.Interrupted.SequenceEqual([existing.Lease!.LeaseId]),
            DuckObserved: duckObserved,
            DuckRestorationObserved: afterExpiry.EffectiveMusicDuckDecibels == 0.0f,
            BusIsolationObserved: uiVoice.IsGranted && sfxVoice.IsGranted,
            UnitTestableWithoutPlayback: true);
        if (!result.Passed)
        {
            throw new InvalidOperationException("Audio mixing policy qualification failed.");
        }

        return result;
    }

    private static AudioCueMixPolicyEntry Entry(
        AudioCue cue,
        string bus,
        int priority,
        int cooldown,
        int polyphony,
        int duration,
        bool interrupt = false,
        float duck = 0.0f,
        string? group = null) => new(
            cue,
            bus,
            priority,
            cooldown,
            polyphony,
            duration,
            interrupt,
            duck,
            group ?? cue.ToString());
}

internal sealed partial class ProceduralCuePlayer : Node
{
    private const int MixRate = 22050;

    private readonly Dictionary<AudioCue, AudioStreamWav> _streams = [];
    private readonly Dictionary<long, AudioStreamPlayer> _voicePlayers = [];
    private readonly AudioMixAllocator _allocator = new(AudioCueMixPolicy.BusCapacities);

    public int CachedStreamCount => _streams.Count;

    public int ActiveVoiceCount => _allocator.ActiveVoiceCount;

    public int PeakVoiceCount { get; private set; }

    public int CooldownSuppressionCount { get; private set; }

    public int PolyphonySuppressionCount { get; private set; }

    public int PrioritySuppressionCount { get; private set; }

    public int InterruptionCount { get; private set; }

    public int MutedSuppressionCount { get; private set; }

    public bool Playing => _voicePlayers.Values.Any(player => player.Playing);

    /// <summary>
    /// Attempts optional cue playback without allowing a missing bus, output,
    /// or backend failure to escape into the gameplay shell.
    /// </summary>
    public bool TryPlayCue(
        AudioCue cue,
        float volumeLinear,
        out string failureReason)
    {
        var policy = AudioCueMixPolicy.For(cue);
        var specification = CueSpecification.For(cue);
        if (AudioServer.GetBusIndex(policy.Bus) < 0)
        {
            failureReason = $"Audio bus unavailable: {policy.Bus}.";
            return false;
        }

        AudioStreamPlayer? voicePlayer = null;
        AudioVoiceLease? grantedLease = null;
        try
        {
            if (!_streams.TryGetValue(cue, out var stream))
            {
                stream = new AudioStreamWav
                {
                    Data = BuildPcm(specification),
                    Format = AudioStreamWav.FormatEnum.Format16Bits,
                    LoopMode = AudioStreamWav.LoopModeEnum.Disabled,
                    MixRate = MixRate,
                    Stereo = true,
                };
                _streams.Add(cue, stream);
            }

            var clamped = Math.Clamp(volumeLinear, 0.0f, 1.0f);
            if (clamped <= 0.0001f)
            {
                MutedSuppressionCount++;
                failureReason = string.Empty;
                return true;
            }

            var nowMilliseconds = ToMixMilliseconds(Time.GetTicksMsec());
            ProcessMix(nowMilliseconds);
            var decision = _allocator.Request(policy.ToRequest(nowMilliseconds));
            ObserveDecision(decision);
            foreach (var interruptedLeaseId in decision.Interrupted)
            {
                StopVoice(interruptedLeaseId);
            }

            if (!decision.IsGranted)
            {
                failureReason = string.Empty;
                return true;
            }

            grantedLease = decision.Lease
                ?? throw new InvalidOperationException("Granted audio decision did not include a lease.");
            voicePlayer = new AudioStreamPlayer
            {
                Bus = policy.Bus,
                Stream = stream,
                VolumeDb = Mathf.LinearToDb(clamped),
            };
            AddChild(voicePlayer);
            _voicePlayers.Add(grantedLease.LeaseId, voicePlayer);
            voicePlayer.Play();
            PeakVoiceCount = Math.Max(PeakVoiceCount, _allocator.ActiveVoiceCount);
            AudioBuses.SetTransientMusicDuck(_allocator.EffectiveMusicDuckDecibels);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            var cleanupFailures = new List<string>();
            if (grantedLease is not null)
            {
                _allocator.Release(grantedLease.LeaseId);
                _voicePlayers.Remove(grantedLease.LeaseId);
            }

            TryStopAfterFailure(voicePlayer, cleanupFailures);
            TryApplyMusicDuck(
                _allocator.EffectiveMusicDuckDecibels,
                cleanupFailures);
            failureReason = $"{exception.GetType().Name}: {exception.Message}";
            if (cleanupFailures.Count > 0)
            {
                failureReason += "; cleanup: " + string.Join(", ", cleanupFailures);
            }

            return false;
        }
    }

    public void ProcessMix(ulong nowMilliseconds) =>
        ProcessMix(ToMixMilliseconds(nowMilliseconds));

    private void ProcessMix(long nowMilliseconds)
    {
        var advance = _allocator.Advance(nowMilliseconds);
        foreach (var expiredLeaseId in advance.ExpiredLeaseIds)
        {
            StopVoice(expiredLeaseId);
        }

        AudioBuses.SetTransientMusicDuck(advance.EffectiveMusicDuckDecibels);
    }

    public void ValidateCue(AudioCue cue)
    {
        var policy = AudioCueMixPolicy.For(cue);
        var specification = CueSpecification.For(cue);
        if (AudioServer.GetBusIndex(policy.Bus) < 0)
        {
            throw new InvalidOperationException(
                $"Procedural cue references an unavailable bus: {policy.Bus}");
        }

        var generatedDurationMilliseconds = (int)MathF.Round(
            specification.DurationSeconds * 1_000.0f);
        if (generatedDurationMilliseconds != policy.ExpectedDurationMilliseconds)
        {
            throw new InvalidOperationException(
                $"Procedural cue duration does not match its mix lease: {cue}.");
        }

        var data = BuildPcm(specification);
        if (data.Length < 8 || data.Length % 4 != 0 || data.All(value => value == 0))
        {
            throw new InvalidOperationException($"Procedural cue generated invalid PCM: {cue}");
        }
    }

    public static ProceduralCueMeasurement MeasureCue(AudioCue cue)
    {
        var policy = AudioCueMixPolicy.For(cue);
        var specification = CueSpecification.For(cue);
        var data = BuildPcm(specification);
        var peak = 0;
        for (var offset = 0; offset < data.Length; offset += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(
                data.AsSpan(offset, sizeof(short)));
            peak = Math.Max(peak, Math.Abs((int)sample));
        }

        var peakLinear = (float)peak / short.MaxValue;
        var peakDecibels = peakLinear <= 0.0f
            ? -80.0f
            : 20.0f * MathF.Log10(peakLinear);
        return new ProceduralCueMeasurement(
            Cue: cue,
            Bus: policy.Bus,
            DurationMilliseconds: policy.ExpectedDurationMilliseconds,
            MixRate: MixRate,
            ChannelCount: 2,
            PeakDecibelsFullScale: peakDecibels,
            PcmSha256: Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
    }

    public bool TryStopAndRelease(out string failureReason)
    {
        var failures = new List<string>();
        StopAllVoices(failures);
        _allocator.Reset();
        TryResetMusicDuck(failures);

        foreach (var stream in _streams.Values)
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add($"dispose: {exception.GetType().Name}");
            }
        }

        _streams.Clear();
        failureReason = string.Join(", ", failures);
        return failures.Count == 0;
    }

    public void StopAndDetach()
    {
        var failures = new List<string>();
        StopAllVoices(failures);
        _allocator.Reset();
        TryResetMusicDuck(failures);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Procedural audio cleanup failed: " + string.Join(", ", failures));
        }
    }

    public void ReleaseStreams()
    {
        StopAndDetach();
        foreach (var stream in _streams.Values)
        {
            stream.Dispose();
        }

        _streams.Clear();
    }

    private void ObserveDecision(AudioMixDecision decision)
    {
        switch (decision.Code)
        {
            case AudioMixDecisionCode.SuppressedByCooldown:
                CooldownSuppressionCount++;
                break;
            case AudioMixDecisionCode.SuppressedByPolyphony:
                PolyphonySuppressionCount++;
                break;
            case AudioMixDecisionCode.SuppressedByPriority:
                PrioritySuppressionCount++;
                break;
            case AudioMixDecisionCode.GrantedWithInterruption:
                InterruptionCount += decision.Interrupted.Count;
                break;
            case AudioMixDecisionCode.Granted:
            case AudioMixDecisionCode.InvalidRequest:
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(decision),
                    decision.Code,
                    "Unknown audio allocation decision.");
        }
    }

    private void StopVoice(long leaseId)
    {
        if (!_voicePlayers.Remove(leaseId, out var player))
        {
            return;
        }

        TryStopAfterFailure(player);
    }

    private void StopAllVoices(List<string>? failures = null)
    {
        foreach (var pair in _voicePlayers.OrderBy(pair => pair.Key).ToArray())
        {
            _voicePlayers.Remove(pair.Key);
            TryStopAfterFailure(pair.Value, failures);
        }
    }

    private static void TryStopAfterFailure(
        AudioStreamPlayer? player,
        List<string>? failures = null)
    {
        if (player is null || !GodotObject.IsInstanceValid(player))
        {
            return;
        }

        try
        {
            player.Stop();
            player.Stream = null;
            player.Free();
        }
        catch (Exception exception)
        {
            failures?.Add($"stop: {exception.GetType().Name}");
        }
    }

    private static long ToMixMilliseconds(ulong milliseconds)
    {
        if (milliseconds > long.MaxValue - AudioMixAllocator.MaximumExpectedDurationMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(milliseconds),
                "Audio mix clock exceeded the supported monotonic range.");
        }

        return (long)milliseconds;
    }

    private static void TryApplyMusicDuck(float decibels, List<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        try
        {
            AudioBuses.SetTransientMusicDuck(decibels);
        }
        catch (Exception exception)
        {
            failures.Add($"duck-apply: {exception.GetType().Name}");
        }
    }

    private static void TryResetMusicDuck(List<string> failures)
    {
        try
        {
            AudioBuses.SetTransientMusicDuck(0.0f);
        }
        catch (Exception exception)
        {
            failures.Add($"duck-reset: {exception.GetType().Name}");
        }
    }

    private static byte[] BuildPcm(CueSpecification specification)
    {
        var frameCount = Math.Max(
            2,
            (int)MathF.Ceiling(specification.DurationSeconds * MixRate));
        var data = new byte[frameCount * 4];
        var phase = 0.0f;
        for (var index = 0; index < frameCount; index++)
        {
            var progress = (float)index / (frameCount - 1);
            var frequency = Mathf.Lerp(
                specification.StartFrequency,
                specification.EndFrequency,
                progress);
            phase += frequency / MixRate;
            var envelope = MathF.Sin(MathF.PI * progress);
            var sample = MathF.Sin(MathF.Tau * phase)
                * envelope
                * specification.Amplitude;
            var pcm = (short)Math.Clamp(
                (int)MathF.Round(sample * short.MaxValue),
                short.MinValue,
                short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * 4, 2), pcm);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan((index * 4) + 2, 2), pcm);
        }

        return data;
    }

    private readonly record struct CueSpecification(
        float StartFrequency,
        float EndFrequency,
        float DurationSeconds,
        float Amplitude)
    {
        public static CueSpecification For(AudioCue cue) => cue switch
        {
            AudioCue.Navigate => new(560.0f, 640.0f, 0.05f, 0.065f),
            AudioCue.Confirm => new(660.0f, 880.0f, 0.07f, 0.09f),
            AudioCue.Back => new(520.0f, 390.0f, 0.07f, 0.08f),
            AudioCue.Pause => new(330.0f, 330.0f, 0.08f, 0.07f),
            AudioCue.Restart => new(300.0f, 900.0f, 0.11f, 0.10f),
            AudioCue.Achievement => new(523.0f, 1046.0f, 0.24f, 0.11f),
            AudioCue.Food => new(740.0f, 1100.0f, 0.09f, 0.10f),
            AudioCue.ComboTier1 => new(700.0f, 840.0f, 0.10f, 0.08f),
            AudioCue.ComboTier2 => new(720.0f, 960.0f, 0.12f, 0.085f),
            AudioCue.ComboTier3 => new(660.0f, 1100.0f, 0.15f, 0.095f),
            AudioCue.ComboTier4 => new(440.0f, 1320.0f, 0.18f, 0.11f),
            AudioCue.ComboBreak => new(600.0f, 220.0f, 0.14f, 0.08f),
            AudioCue.Starvation => new(420.0f, 190.0f, 0.20f, 0.09f),
            AudioCue.Collision => new(180.0f, 70.0f, 0.18f, 0.11f),
            AudioCue.StarvationDeath => new(300.0f, 55.0f, 0.22f, 0.12f),
            AudioCue.ShieldSpawn => new(510.0f, 760.0f, 0.14f, 0.08f),
            AudioCue.ShieldActivate => new(580.0f, 1160.0f, 0.18f, 0.10f),
            AudioCue.PhaseShiftActivate => new(900.0f, 450.0f, 0.17f, 0.08f),
            AudioCue.LastStandActivate => new(240.0f, 960.0f, 0.21f, 0.11f),
            AudioCue.SlowMoActivate => new(520.0f, 180.0f, 0.19f, 0.09f),
            AudioCue.BoostActivate => new(420.0f, 1400.0f, 0.13f, 0.11f),
            AudioCue.MagnetActivate => new(180.0f, 720.0f, 0.16f, 0.09f),
            AudioCue.BaitActivate => new(1100.0f, 880.0f, 0.12f, 0.075f),
            AudioCue.GluttonyActivate => new(780.0f, 520.0f, 0.15f, 0.10f),
            AudioCue.SegmentDetachActivate => new(1200.0f, 240.0f, 0.20f, 0.11f),
            AudioCue.ShieldExpire => new(620.0f, 310.0f, 0.16f, 0.07f),
            AudioCue.ShieldBreak => new(980.0f, 180.0f, 0.22f, 0.11f),
            AudioCue.PowerSpawn => new(480.0f, 720.0f, 0.13f, 0.08f),
            AudioCue.PowerExpire => new(560.0f, 280.0f, 0.14f, 0.07f),
            AudioCue.PowerRecovery => new(360.0f, 920.0f, 0.22f, 0.12f),
            AudioCue.Victory => new(440.0f, 1320.0f, 0.24f, 0.10f),
            _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, "Unknown audio cue."),
        };
    }
}
