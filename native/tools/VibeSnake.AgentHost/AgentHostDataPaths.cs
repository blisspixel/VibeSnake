namespace VibeSnake.AgentHost;

public static class AgentHostDataPaths
{
    public static string ResolveGodotUserDataRoot()
    {
        var platform = OperatingSystem.IsWindows()
            ? AgentHostPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? AgentHostPlatform.MacOS
                : AgentHostPlatform.Linux;
        var applicationData = Environment.GetFolderPath(platform == AgentHostPlatform.Windows
            ? Environment.SpecialFolder.ApplicationData
            : Environment.SpecialFolder.LocalApplicationData);
        return ResolveGodotUserDataRoot(applicationData, platform);
    }

    internal static string ResolveGodotUserDataRoot(
        string applicationData,
        AgentHostPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(applicationData)
            || !Path.IsPathFullyQualified(applicationData))
        {
            throw new InvalidOperationException(
                "The platform application-data directory is unavailable.");
        }

        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform));
        }

        var platformRoot = platform == AgentHostPlatform.Linux
            ? Path.Combine(applicationData, "godot", "app_userdata")
            : Path.Combine(applicationData, "Godot", "app_userdata");
        return Path.GetFullPath(Path.Combine(platformRoot, "Vibe Snake"));
    }
}

internal enum AgentHostPlatform : byte
{
    Windows = 0,
    MacOS = 1,
    Linux = 2,
}
