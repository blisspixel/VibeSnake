using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ControllerConnectionTrackerTests
{
    [Fact]
    public void Notes_connect_then_disconnect_without_owning_presentation_copy()
    {
        var tracker = new ControllerConnectionTracker();
        var connected = tracker.NoteConnected(0, "Xbox Controller");

        Assert.NotNull(connected);
        Assert.Equal(ControllerConnectionKind.Connected, connected.Value.Kind);
        Assert.Equal(0, connected.Value.DeviceId);
        Assert.Equal("Xbox Controller", connected.Value.DeviceName);
        Assert.True(tracker.IsConnected(0));
        Assert.Equal(1, tracker.ConnectedCount);

        Assert.Null(tracker.NoteConnected(0, "Xbox Controller"));

        var disconnected = tracker.NoteDisconnected(0);
        Assert.NotNull(disconnected);
        Assert.Equal(ControllerConnectionKind.Disconnected, disconnected.Value.Kind);
        Assert.Equal("Xbox Controller", disconnected.Value.DeviceName);
        Assert.False(tracker.IsConnected(0));
        Assert.Equal(0, tracker.ConnectedCount);
        Assert.Null(tracker.NoteDisconnected(0));
    }

    [Fact]
    public void Rejects_negative_device_ids()
    {
        var tracker = new ControllerConnectionTracker();
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.NoteConnected(-1, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.NoteDisconnected(-1));
    }

    [Fact]
    public void Normalizes_blank_and_control_characters_in_device_names()
    {
        var tracker = new ControllerConnectionTracker();
        var blank = tracker.NoteConnected(1, "  ");
        Assert.Equal("Controller", blank!.Value.DeviceName);

        var dirty = tracker.NoteConnected(2, "Pad\u0001Name\u0007");
        Assert.Equal("PadName", dirty!.Value.DeviceName);
    }

    [Fact]
    public void Truncates_long_device_names()
    {
        var tracker = new ControllerConnectionTracker();
        var longName = new string('A', ControllerConnectionTracker.MaximumDeviceNameCharacters + 20);
        var connected = tracker.NoteConnected(3, longName);
        Assert.Equal(
            ControllerConnectionTracker.MaximumDeviceNameCharacters,
            connected!.Value.DeviceName.Length);
    }

    [Fact]
    public void Stops_tracking_beyond_capacity()
    {
        var tracker = new ControllerConnectionTracker();
        for (var index = 0; index < ControllerConnectionTracker.MaximumTrackedDevices; index++)
        {
            Assert.NotNull(tracker.NoteConnected(index, "Pad " + index));
        }

        Assert.Null(tracker.NoteConnected(ControllerConnectionTracker.MaximumTrackedDevices, "Overflow"));
        Assert.Equal(ControllerConnectionTracker.MaximumTrackedDevices, tracker.ConnectedCount);
    }
}
