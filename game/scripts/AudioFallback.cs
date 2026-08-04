using System.Buffers.Binary;
using Godot;

namespace VibeSnake.Game;

internal enum AudioCue
{
    Confirm,
    Back,
    Pause,
    Food,
    ShieldSpawn,
    ShieldActivate,
    ShieldExpire,
    ShieldBreak,
    PowerSpawn,
    PowerActivate,
    PowerExpire,
    PowerRecovery,
    Death,
    Victory,
}

internal static class AudioBuses
{
    public const string Music = "Music";
    public const string Sfx = "SFX";
    public const string Ui = "UI";

    public static void EnsureRegistered()
    {
        EnsureBus(Music);
        EnsureBus(Sfx);
        EnsureBus(Ui);
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

        SetBusLinear("Master", settings.MasterMuted ? 0.0f : settings.MasterVolume);
        SetBusLinear(Music, settings.MusicMuted ? 0.0f : settings.MusicVolume);
        SetBusLinear(Sfx, settings.SfxMuted ? 0.0f : settings.SfxVolume);
        SetBusLinear(Ui, settings.UiMuted ? 0.0f : settings.UiVolume);
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
        AudioServer.SetBusSend(index, "Master");
    }
}

internal sealed partial class ProceduralCuePlayer : AudioStreamPlayer
{
    private const int MixRate = 22050;

    private readonly Dictionary<AudioCue, AudioStreamWav> _streams = [];

    public void PlayCue(AudioCue cue)
    {
        var specification = CueSpecification.For(cue);
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

        Stop();
        Bus = specification.Bus;
        Stream = stream;
        Play();
    }

    public void ValidateCue(AudioCue cue)
    {
        var specification = CueSpecification.For(cue);
        if (AudioServer.GetBusIndex(specification.Bus) < 0)
        {
            throw new InvalidOperationException(
                $"Procedural cue references an unavailable bus: {specification.Bus}");
        }

        var data = BuildPcm(specification);
        if (data.Length < 8 || data.Length % 4 != 0 || data.All(value => value == 0))
        {
            throw new InvalidOperationException($"Procedural cue generated invalid PCM: {cue}");
        }
    }

    public void StopAndRelease()
    {
        StopAndDetach();
        ReleaseStreams();
    }

    public void StopAndDetach()
    {
        Stop();
        Stream = null;
    }

    public void ReleaseStreams()
    {
        foreach (var stream in _streams.Values)
        {
            stream.Dispose();
        }

        _streams.Clear();
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
        float Amplitude,
        string Bus)
    {
        public static CueSpecification For(AudioCue cue) => cue switch
        {
            AudioCue.Confirm => new(660.0f, 880.0f, 0.07f, 0.09f, AudioBuses.Ui),
            AudioCue.Back => new(520.0f, 390.0f, 0.07f, 0.08f, AudioBuses.Ui),
            AudioCue.Pause => new(330.0f, 330.0f, 0.08f, 0.07f, AudioBuses.Ui),
            AudioCue.Food => new(740.0f, 1100.0f, 0.09f, 0.10f, AudioBuses.Sfx),
            AudioCue.ShieldSpawn => new(510.0f, 760.0f, 0.14f, 0.08f, AudioBuses.Sfx),
            AudioCue.ShieldActivate => new(580.0f, 1160.0f, 0.18f, 0.10f, AudioBuses.Sfx),
            AudioCue.ShieldExpire => new(620.0f, 310.0f, 0.16f, 0.07f, AudioBuses.Sfx),
            AudioCue.ShieldBreak => new(980.0f, 180.0f, 0.22f, 0.11f, AudioBuses.Sfx),
            AudioCue.PowerSpawn => new(480.0f, 720.0f, 0.13f, 0.08f, AudioBuses.Sfx),
            AudioCue.PowerActivate => new(640.0f, 1080.0f, 0.16f, 0.10f, AudioBuses.Sfx),
            AudioCue.PowerExpire => new(560.0f, 280.0f, 0.14f, 0.07f, AudioBuses.Sfx),
            AudioCue.PowerRecovery => new(360.0f, 920.0f, 0.22f, 0.12f, AudioBuses.Sfx),
            AudioCue.Death => new(220.0f, 90.0f, 0.20f, 0.11f, AudioBuses.Sfx),
            AudioCue.Victory => new(440.0f, 1320.0f, 0.24f, 0.10f, AudioBuses.Sfx),
            _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, "Unknown audio cue."),
        };
    }
}
