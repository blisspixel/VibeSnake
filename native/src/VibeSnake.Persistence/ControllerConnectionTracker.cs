namespace VibeSnake.Persistence;

/// <summary>
/// Pure controller connection ledger for hot-plug notices.
/// Presentation layers report device ids; this type owns dedupe, captions,
/// and disconnect safety without referencing Godot or OS APIs.
/// </summary>
public enum ControllerConnectionKind : byte
{
    Connected = 0,
    Disconnected = 1,
}

public readonly record struct ControllerConnectionEvent(
    ControllerConnectionKind Kind,
    int DeviceId,
    string DeviceName,
    string Caption);

public sealed class ControllerConnectionTracker
{
    public const int MaximumTrackedDevices = 16;
    public const int MaximumDeviceNameCharacters = 64;

    private readonly Dictionary<int, string> _connected = [];

    public int ConnectedCount => _connected.Count;

    public IReadOnlyCollection<int> ConnectedDeviceIds => _connected.Keys;

    /// <summary>
    /// Records a newly connected controller. Returns null when the device was
    /// already tracked or capacity is exhausted.
    /// </summary>
    public ControllerConnectionEvent? NoteConnected(int deviceId, string? deviceName)
    {
        if (deviceId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceId));
        }

        if (_connected.ContainsKey(deviceId))
        {
            return null;
        }

        if (_connected.Count >= MaximumTrackedDevices)
        {
            return null;
        }

        var name = NormalizeDeviceName(deviceName);
        _connected[deviceId] = name;
        return new ControllerConnectionEvent(
            ControllerConnectionKind.Connected,
            deviceId,
            name,
            "CONTROLLER CONNECTED: " + name);
    }

    /// <summary>
    /// Records a disconnect. Returns null when the device was not tracked.
    /// </summary>
    public ControllerConnectionEvent? NoteDisconnected(int deviceId)
    {
        if (deviceId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceId));
        }

        if (!_connected.Remove(deviceId, out var name))
        {
            return null;
        }

        return new ControllerConnectionEvent(
            ControllerConnectionKind.Disconnected,
            deviceId,
            name,
            "CONTROLLER DISCONNECTED: " + name);
    }

    public bool IsConnected(int deviceId) => _connected.ContainsKey(deviceId);

    public string? GetDeviceName(int deviceId) =>
        _connected.TryGetValue(deviceId, out var name) ? name : null;

    private static string NormalizeDeviceName(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return "Controller";
        }

        var trimmed = deviceName.Trim();
        if (trimmed.Length > MaximumDeviceNameCharacters)
        {
            trimmed = trimmed[..MaximumDeviceNameCharacters];
        }

        // Strip control characters that could corrupt captions or logs.
        var buffer = new char[trimmed.Length];
        var length = 0;
        foreach (var character in trimmed)
        {
            if (char.IsControl(character))
            {
                continue;
            }

            buffer[length++] = character;
        }

        return length == 0 ? "Controller" : new string(buffer, 0, length);
    }
}
