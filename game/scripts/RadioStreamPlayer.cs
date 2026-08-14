using Godot;
using VibeSnake.Persistence;

namespace VibeSnake.Game;

internal readonly record struct RadioStreamRuntimeSnapshot(
    bool PlayerReady,
    bool Playing,
    string? TrackId,
    double PlaybackPositionSeconds,
    string? LastFailure);

/// <summary>
/// Godot decoder adapter for the playback-free radio policy. It loads only the
/// current validated MP3 payload, reports failures back to policy, and never
/// touches rules or replay state.
/// </summary>
internal sealed partial class RadioStreamPlayer : Node
{
    private AudioStreamPlayer? _player;
    private RadioPlaybackPolicy? _policy;
    private OptionalPackStore? _store;
    private ContentInventory? _inventory;
    private IReadOnlyDictionary<string, string>? _checkoutSourcePaths;
    private string? _loadedTrackId;
    private string? _lastFailure;
    private bool _suppressFinished;

    public override void _Ready()
    {
        if (_player is not null)
        {
            return;
        }

        _player = new AudioStreamPlayer
        {
            Name = "RadioVoice",
            Bus = AudioBuses.Music,
        };
        _player.Finished += OnFinished;
        AddChild(_player);
        // Configure can run while this node is still entering the tree. Retry
        // here so an early catalog never leaves the decoder permanently idle.
        Synchronize();
    }

    public void Configure(
        RadioPlaybackPolicy policy,
        OptionalPackStore store,
        ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(inventory);
        _policy = policy;
        _store = store;
        _inventory = inventory;
        _checkoutSourcePaths = null;
        Synchronize();
    }

    public void ConfigureCheckout(
        RadioPlaybackPolicy policy,
        IReadOnlyDictionary<string, string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        _policy = policy;
        _store = null;
        _inventory = null;
        _checkoutSourcePaths = sourcePaths;
        Synchronize();
    }

    public void Synchronize()
    {
        if (_player is null
            || _policy is null
            || (_checkoutSourcePaths is null && (_store is null || _inventory is null)))
        {
            return;
        }

        var snapshot = _policy.Snapshot;
        if (snapshot.Mode == RadioPlaybackMode.Paused)
        {
            if (_loadedTrackId == snapshot.TrackId && _player.Playing)
            {
                _player.StreamPaused = true;
            }

            return;
        }

        if (snapshot.Mode != RadioPlaybackMode.Playing || snapshot.TrackId is null)
        {
            StopCurrent();
            return;
        }

        if (_loadedTrackId == snapshot.TrackId && _player.Playing)
        {
            _player.StreamPaused = false;
            return;
        }

        var attemptsRemaining = Math.Max(snapshot.PlayableTrackCount, 1);
        while (attemptsRemaining-- > 0)
        {
            snapshot = _policy.Snapshot;
            if (snapshot.TrackId is null || snapshot.PackId is null)
            {
                StopCurrent();
                return;
            }

            byte[]? bytes = null;
            if (_checkoutSourcePaths is not null)
            {
                if (_checkoutSourcePaths.TryGetValue(snapshot.TrackId, out var sourcePath))
                {
                    try
                    {
                        bytes = File.ReadAllBytes(sourcePath);
                    }
                    catch (Exception exception) when (
                        exception is IOException
                            or UnauthorizedAccessException
                            or ArgumentException)
                    {
                        bytes = null;
                    }
                }
            }
            else if (_store is not null && _inventory is not null)
            {
                var read = _store.ReadAsset(snapshot.PackId, snapshot.TrackId, _inventory);
                bytes = read.IsSuccess ? read.Asset?.Bytes : null;
            }

            if (bytes is null)
            {
                _lastFailure = "Radio track bytes were unavailable.";
                _policy.NoteTrackUnavailable(snapshot.TrackId);
                continue;
            }

            try
            {
                var stream = AudioStreamMP3.LoadFromBuffer(bytes);
                if (stream is null || stream.GetLength() <= 0.0)
                {
                    stream?.Dispose();
                    _lastFailure = "Godot rejected an empty or invalid MP3 stream.";
                    _policy.NoteTrackUnavailable(snapshot.TrackId);
                    continue;
                }

                StopCurrent();
                _player.Stream = stream;
                _loadedTrackId = snapshot.TrackId;
                _player.Play();
                _lastFailure = null;
                return;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or IOException)
            {
                _lastFailure = $"{exception.GetType().Name}: {exception.Message}";
                _policy.NoteTrackUnavailable(snapshot.TrackId);
            }
        }

        StopCurrent();
    }

    public void ForceReload() => StopCurrent();

    public RadioStreamRuntimeSnapshot CaptureRuntimeSnapshot() => new(
        PlayerReady: _player is not null,
        Playing: _player?.Playing ?? false,
        TrackId: _loadedTrackId,
        PlaybackPositionSeconds: _player?.GetPlaybackPosition() ?? 0.0,
        LastFailure: _lastFailure);

    public bool TryStopAndRelease(out string? failure)
    {
        failure = null;
        try
        {
            StopCurrent();
            if (_player is not null)
            {
                _player.Finished -= OnFinished;
                _player.QueueFree();
                _player = null;
            }

            _policy = null;
            _store = null;
            _inventory = null;
            _checkoutSourcePaths = null;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
            failure = exception.Message;
            return false;
        }
    }

    private void OnFinished()
    {
        if (_suppressFinished || _policy is null)
        {
            return;
        }

        _loadedTrackId = null;
        _policy.OnTrackEnded();
        Synchronize();
    }

    private void StopCurrent()
    {
        if (_player is null)
        {
            _loadedTrackId = null;
            return;
        }

        _suppressFinished = true;
        try
        {
            _player.Stop();
            _player.StreamPaused = false;
            var stream = _player.Stream;
            _player.Stream = null;
            stream?.Dispose();
            _loadedTrackId = null;
        }
        finally
        {
            _suppressFinished = false;
        }
    }
}
