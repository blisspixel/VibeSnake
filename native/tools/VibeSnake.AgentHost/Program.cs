using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VibeSnake.Persistence;

namespace VibeSnake.AgentHost;

public static class Program
{
    public const string HostName = "vibesnake-agent-host";
    public const string HostVersion = "0.6.0";
    public const string McpProtocolVersion = "2026-07-28";

    public static async Task Main(string[] args)
    {
        var host = CreateHostApplicationBuilder(args).Build();
        await host.RunAsync().ConfigureAwait(false);
    }

    internal static HostApplicationBuilder CreateHostApplicationBuilder(
        string[] args,
        string? userDataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        var replayStore = new ReplayStore(
            userDataRoot ?? AgentHostDataPaths.ResolveGodotUserDataRoot());
        var registry = new AgentSessionRegistry(replayStore);
        var tools = new McpAgentTools(registry);
        var serializerOptions = CreateSerializerOptions();

        builder.Services.AddSingleton(registry);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ProtocolVersion = McpProtocolVersion;
                options.InitializationTimeout = TimeSpan.FromSeconds(45);
                options.ServerInfo = new Implementation
                {
                    Name = HostName,
                    Version = HostVersion,
                };
            })
            .WithStdioServerTransport()
            .WithTools(tools, serializerOptions)
            .WithResources<AgentResources>();
        return builder;
    }

    internal static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            AllowDuplicateProperties = false,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.SnakeCaseLower,
                allowIntegerValues: false));
        return options;
    }
}
