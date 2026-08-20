namespace VibeSnake.AgentHost;

public static class AgentHostDataPaths
{
    public const string UserDataRootEnvironmentVariable = "VIBESNAKE_AGENT_USER_DATA_ROOT";

    public static string ResolveUserDataRoot()
    {
        var processDirectory = string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath));
        return ResolveUserDataRoot(
            Environment.GetEnvironmentVariable(UserDataRootEnvironmentVariable),
            processDirectory);
    }

    internal static string ResolveUserDataRoot(string? overrideRoot, string? processDirectory)
    {
        if (string.IsNullOrWhiteSpace(overrideRoot))
        {
            return ResolveGodotUserDataRoot();
        }

        if (!Path.IsPathFullyQualified(overrideRoot))
        {
            throw new InvalidOperationException(
                "VIBESNAKE_AGENT_USER_DATA_ROOT must be a fully qualified directory.");
        }

        var resolved = Path.GetFullPath(overrideRoot);
        if (!Directory.Exists(resolved))
        {
            throw new InvalidOperationException(
                "VIBESNAKE_AGENT_USER_DATA_ROOT must be an existing directory.");
        }

        if (!string.IsNullOrWhiteSpace(processDirectory)
            && IsSameOrContainedPath(resolved, processDirectory))
        {
            throw new InvalidOperationException(
                "VIBESNAKE_AGENT_USER_DATA_ROOT must not be the host package directory.");
        }

        return resolved;
    }

    internal static bool IsSameOrContainedPath(string candidate, string container)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var containerPrefix = Path.GetFullPath(container).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidatePrefix = Path.GetFullPath(candidate).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidatePrefix.Equals(containerPrefix, comparison)
            || candidatePrefix.StartsWith(containerPrefix, comparison);
    }

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
