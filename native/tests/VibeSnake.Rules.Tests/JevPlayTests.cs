using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VibeSnake.AgentPlay;
using VibeSnake.JevPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class JevPlayTests
{
    [Fact]
    public async Task Entry_point_prints_usage_for_help()
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            Assert.Equal(0, await JevPlayEntry.Main(["help"]));
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        Assert.Contains("watch", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Endpoints_pin_openrouter_and_typesafe_and_reject_a_local_file_url()
    {
        var decisions = JevEndpoint.OpenRouterDecisions();
        var systemOne = JevEndpoint.OpenRouterSystemOne("~typesafe/jev-latest");
        var hosted = JevEndpoint.TypeSafeHosted();
        var local = JevEndpoint.Local(new Uri("https://127.0.0.1:8099/v1/systemone"));

        Assert.Equal(JevProviderKind.OpenRouterDecisions, decisions.Kind);
        Assert.Equal(JevProviderKind.OpenRouterSystemOne, systemOne.Kind);
        Assert.Equal(JevProviderKind.TypeSafeHosted, hosted.Kind);
        Assert.Equal(JevProviderKind.Local, local.Kind);
        Assert.Equal("https://openrouter.ai/api/alpha/decisions", decisions.Address.AbsoluteUri);
        Assert.Equal("https://openrouter.ai/api/v1/systemone", systemOne.Address.AbsoluteUri);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", hosted.Address.AbsoluteUri);
        Assert.Equal(JevEndpoint.PinnedOpenRouterModel, decisions.Model);
        Assert.Equal(JevEndpoint.LatestOpenRouterModel, systemOne.Model);
        Assert.Equal(JevEndpoint.PinnedTypeSafeModel, hosted.Model);
        Assert.Equal("local-jev", local.Model);
        Assert.Equal(JevEndpoint.OpenRouterKeyVariable, decisions.ApiKeyVariable);
        Assert.Equal(JevEndpoint.TypeSafeKeyVariable, hosted.ApiKeyVariable);
        Assert.Null(local.ApiKeyVariable);
        Assert.Throws<ArgumentNullException>(() => JevEndpoint.Local(null!));
        Assert.Throws<ArgumentException>(() => JevEndpoint.Local(new Uri("ftp://127.0.0.1/jev")));
        Assert.Throws<ArgumentException>(() => JevEndpoint.OpenRouterDecisions("bad model"));
        Assert.Throws<ArgumentException>(() => JevEndpoint.TypeSafeHosted(new string('m', 129)));
        using var handler = JevPlayCommand.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void Describe_reports_heading_relative_cells_and_stops_after_death()
    {
        var config = new RunConfig(Width: 5, Height: 4, StarvationTicks: 1);
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(2, 0), new GridPoint(2, 1)],
            Direction.Right,
            new GridPoint(3, 1),
            hungerTicksRemaining: 1);
        using (var described = JsonDocument.Parse(JevSnakeAdvisor.Describe(run)))
        {
            var root = described.RootElement;
            Assert.Equal("running", root.GetProperty("status").GetString());
            Assert.Equal("right", root.GetProperty("heading").GetString());
            Assert.Equal(2, root.GetProperty("length").GetInt32());
            Assert.Equal(3, root.GetProperty("food").GetProperty("x").GetInt32());
            Assert.Equal(1, root.GetProperty("food").GetProperty("y").GetInt32());
            Assert.Equal("food", root.GetProperty("ahead").GetString());
            Assert.Equal("body", root.GetProperty("left").GetString());
            Assert.Equal("empty", root.GetProperty("right").GetString());
        }

        var bare = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(2, 1)],
            Direction.Right,
            null,
            hungerTicksRemaining: 1);
        using (var described = JsonDocument.Parse(JevSnakeAdvisor.Describe(bare)))
        {
            Assert.Equal(JsonValueKind.Null, described.RootElement.GetProperty("food").ValueKind);
            Assert.Equal("empty", described.RootElement.GetProperty("ahead").GetString());
        }

        Assert.Equal(["continue", "up", "down"], JevSnakeAdvisor.LegalActions(run));
        _ = bare.Step();
        Assert.Equal(RunStatus.Dead, bare.Status);
        Assert.Empty(JevSnakeAdvisor.LegalActions(bare));
        Assert.Throws<ArgumentNullException>(() => JevSnakeAdvisor.Describe(null!));
        Assert.Throws<ArgumentNullException>(() => JevSnakeAdvisor.LegalActions(null!));
    }

    [Fact]
    public async Task Choice_uses_only_legal_actions_and_low_confidence_continues()
    {
        var run = SnakeRun.Create(7);
        var handler = new ScriptHandler(
            """
            {"model":"typesafe/jev-1.13","answers":{"action":{"choice":"up","probabilities":{"up":0.91}},"intent":{"choice":"seek_food"}},"usage":{"cost":0.25,"input_tokens":10,"output_tokens":0}}
            """);
        using var http = new HttpClient(handler);
        var client = new JevClient(
            JevEndpoint.Local(new Uri("http://127.0.0.1:8099/v1/systemone")),
            http,
            static _ => null);
        var step = await JevSnakeAdvisor.ChooseAsync(client, run, 0.34, CancellationToken.None);

        Assert.Equal("up", step.Action);
        Assert.Equal("up", step.ModelChoice);
        Assert.Equal("seek_food", step.Intent);
        Assert.Equal(0.91, step.Probability);
        Assert.Equal(0.25, step.CostUsd);
        Assert.Equal("typesafe/jev-1.13", step.Model);
        Assert.Null(handler.Authorization);
        Assert.Equal(Direction.Right, run.Direction);
        Assert.False(run.QueueDirection(Direction.Up));
        _ = run.Step();
        Assert.Equal(Direction.Up, run.Direction);
        using var request = JsonDocument.Parse(handler.Body!);
        var questions = request.RootElement.GetProperty("questions");
        var names = questions.GetProperty("action").GetProperty("criteria")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(JevSnakeAdvisor.LegalActions(SnakeRun.Create(7)), names);
        Assert.DoesNotContain("right", names);
        Assert.DoesNotContain("left", names);
        Assert.Equal(
            ["seek_food", "seek_power", "preserve_space", "take_risk", "recover"],
            questions.GetProperty("intent").GetProperty("criteria").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("local-jev", request.RootElement.GetProperty("model").GetString());

        var cautious = SnakeRun.Create(7);
        handler.Response =
            """
            {"answers":{"action":{"choice":"up","probabilities":{"up":0.10}},"intent":{"choice":"take_risk"}}}
            """;
        var held = await JevSnakeAdvisor.ChooseAsync(client, cautious, 0.34, CancellationToken.None);
        Assert.Equal("continue", held.Action);
        Assert.Equal("up", held.ModelChoice);
        Assert.Equal(0.10, held.Probability);
        Assert.Equal(Direction.Right, cautious.Direction);
        Assert.True(cautious.QueueDirection(Direction.Up));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            JevSnakeAdvisor.ChooseAsync(client, cautious, 1.1, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_key_illegal_choice_and_http_failure_do_not_leak_the_key()
    {
        var handler = new ScriptHandler("""{"answers":{"action":{"choice":"left"}}}""");
        using var http = new HttpClient(handler);
        var client = new JevClient(
            JevEndpoint.OpenRouterDecisions(),
            http,
            static _ => null);
        var missing = await Assert.ThrowsAsync<JevDecisionException>(() =>
            client.AskAsync("{}", ["continue"], CancellationToken.None));
        Assert.Contains("OPENROUTER_API_KEY", missing.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);

        var local = new JevClient(
            JevEndpoint.Local(new Uri("http://127.0.0.1:8099/v1/systemone")),
            http,
            static _ => "secret-key");
        var run = SnakeRun.Create(3);
        var illegal = await JevSnakeAdvisor.ChooseAsync(local, run, 0, CancellationToken.None);
        Assert.Equal("continue", illegal.Action);
        Assert.Equal("left", illegal.ModelChoice);
        Assert.Equal("undeclared", illegal.Intent);
        Assert.Null(illegal.CostUsd);
        Assert.Null(handler.Authorization);
        Assert.Equal(Direction.Right, run.Direction);
        Assert.True(run.QueueDirection(Direction.Up));

        handler.Status = HttpStatusCode.Unauthorized;
        handler.Response = "rejected secret-key\nnext";
        var hosted = new JevClient(
            JevEndpoint.OpenRouterDecisions(),
            http,
            static _ => "secret-key");
        var failed = await Assert.ThrowsAsync<JevDecisionException>(() =>
            hosted.AskAsync("""{"tick":1}""", ["continue", "up"], CancellationToken.None));
        Assert.Contains("[redacted]", failed.Message, StringComparison.Ordinal);
        Assert.Contains("401", failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", failed.Message, StringComparison.Ordinal);
        Assert.Equal("Bearer secret-key", handler.Authorization);

        handler.Status = HttpStatusCode.OK;
        handler.Response = """{"answers":{"action":{"choice":"continue","probabilities":{"continue":1.4}}},"usage":{"cost":-1}}""";
        var clamped = await hosted.AskAsync("""{"tick":1}""", ["continue"], CancellationToken.None);
        Assert.Equal(1, clamped.Probability);
        Assert.Null(clamped.CostUsd);
        Assert.Equal(JevEndpoint.PinnedOpenRouterModel, clamped.Model);

        handler.Response = """{"answers":{"action":{"choice":"continue","confidence":0.5}}}""";
        var confidence = await hosted.AskAsync("""{"tick":1}""", ["continue"], CancellationToken.None);
        Assert.Equal(0.5, confidence.Probability);

        handler.Response = """{"answers":{"action":{"choice":1}}}""";
        var unreadable = await Assert.ThrowsAsync<JevDecisionException>(() =>
            hosted.AskAsync("""{"tick":1}""", ["continue"], CancellationToken.None));
        Assert.Contains("action choice", unreadable.Message, StringComparison.Ordinal);

        var malformed = await Assert.ThrowsAsync<JevDecisionException>(() =>
            hosted.AskAsync("{", ["continue"], CancellationToken.None));
        Assert.Contains("must be JSON", malformed.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            hosted.AskAsync("{}", [], CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JevClient(JevEndpoint.OpenRouterDecisions(), http, timeout: TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new JevClient(null!, http));
    }

    [Fact]
    public async Task Watch_plays_a_scripted_local_model_and_stops_when_the_run_ends()
    {
        var handler = new ScriptHandler(
            """
            {"model":"local-jev","answers":{"action":{"choice":"continue","confidence":1},"intent":{"choice":"preserve_space"}},"usage":{"cost":0.25}}
            """);
        using var http = new HttpClient(handler);
        var client = new JevClient(
            JevEndpoint.Local(new Uri("http://127.0.0.1:8099/v1/systemone"), "local-jev"),
            http);
        var report = await JevWatch.PlayAsync(client, 11, 4, 0.34, CancellationToken.None);

        Assert.Equal(11UL, report.Seed);
        Assert.Equal(4, report.Steps);
        Assert.Equal(RunStatus.Running, report.Status);
        Assert.Equal(1.0, report.CostUsd);
        Assert.All(report.Lines, line => Assert.Equal("continue", line.Action));
        Assert.Contains(
            "probability=1.000",
            JevSnakeAdvisor.FormatStep(report.Lines[0]),
            StringComparison.Ordinal);
        Assert.Contains("choice=continue", JevSnakeAdvisor.FormatStep(report.Lines[0]), StringComparison.Ordinal);

        handler.Response = """{"answers":{"action":{"choice":"continue","confidence":1}}}""";
        var dying = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 1),
            [new GridPoint(2, 1)],
            Direction.Right,
            new GridPoint(0, 0),
            hungerTicksRemaining: 1);
        var ended = await JevWatch.PlayAsync(client, dying, 11, 5, 0.34, CancellationToken.None);
        Assert.Equal(RunStatus.Dead, ended.Status);
        Assert.Equal(1, ended.Steps);
        Assert.Null(ended.CostUsd);
        Assert.Equal(11UL, ended.Seed);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            JevWatch.PlayAsync(client, 1, 0, 0.34, CancellationToken.None));

        handler.Status = HttpStatusCode.OK;
        handler.Response =
            """
            {"model":"local-jev","answers":{"action":{"choice":"continue","confidence":1},"intent":{"choice":"preserve_space"}},"usage":{"cost":0.25}}
            """;
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await JevPlayCommand.RunAsync(
            ["watch", "--route", "local", "--endpoint", "http://127.0.0.1:8099/v1/systemone", "--steps", "2", "--seed", "11"],
            output,
            error,
            _ => http);
        Assert.Equal(0, code);
        Assert.Contains("status=Running", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("costUsd=0.500000", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());

        var usage = new StringWriter();
        var usageError = new StringWriter();
        Assert.Equal(2, await JevPlayCommand.RunAsync([], usage, usageError));
        Assert.Contains("watch", usage.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, await JevPlayCommand.RunAsync(["help"], usage, usageError));
        Assert.Equal(2, await JevPlayCommand.RunAsync(["watch", "--route", "local"], usage, usageError));
        Assert.Contains("requires --endpoint", usageError.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, await JevPlayCommand.RunAsync(["play"], usage, usageError));
        Assert.Equal(2, await JevPlayCommand.RunAsync(["watch", "--nope", "1"], usage, usageError));
        Assert.Equal(2, await JevPlayCommand.RunAsync(["watch", "--seed"], usage, usageError));
        Assert.Equal(2, await JevPlayCommand.RunAsync(["watch", "--seed", "-1"], usage, usageError));
        Assert.Equal(2, await JevPlayCommand.RunAsync(["watch", "--steps", "0"], usage, usageError));
        Assert.Equal(2, await JevPlayCommand.RunAsync(["watch", "--steps", "2001"], usage, usageError));
        Assert.Equal(2, await JevPlayCommand.RunAsync(["watch", "--min-probability", "2"], usage, usageError));
        Assert.Equal(2, await JevPlayCommand.RunAsync(["watch", "--endpoint", "not a url"], usage, usageError));
        Assert.Equal(
            2,
            await JevPlayCommand.RunAsync(["watch", "--route", "remote", "--endpoint", "http://127.0.0.1/jev"], usage, usageError));

        var seen = new List<string>();
        var previousOpenRouter = Environment.GetEnvironmentVariable(JevEndpoint.OpenRouterKeyVariable);
        var previousTypeSafe = Environment.GetEnvironmentVariable(JevEndpoint.TypeSafeKeyVariable);
        Environment.SetEnvironmentVariable(JevEndpoint.OpenRouterKeyVariable, "test-openrouter-key");
        Environment.SetEnvironmentVariable(JevEndpoint.TypeSafeKeyVariable, "test-typesafe-key");
        try
        {
            Assert.Equal(
                0,
                await JevPlayCommand.RunAsync(
                    ["watch", "--route", "openrouter-systemone", "--model", "typesafe/jev-1.13", "--steps", "1"],
                    usage,
                    usageError,
                    endpoint =>
                    {
                        seen.Add(endpoint.Address.AbsoluteUri);
                        return http;
                    }));
            Assert.Equal(
                0,
                await JevPlayCommand.RunAsync(
                    ["watch", "--route", "typesafe", "--steps", "1"],
                    usage,
                    usageError,
                    endpoint =>
                    {
                        seen.Add(endpoint.Address.AbsoluteUri);
                        return http;
                    }));
        }
        finally
        {
            Environment.SetEnvironmentVariable(JevEndpoint.OpenRouterKeyVariable, previousOpenRouter);
            Environment.SetEnvironmentVariable(JevEndpoint.TypeSafeKeyVariable, previousTypeSafe);
        }

        Assert.Equal(
            ["https://openrouter.ai/api/v1/systemone", "https://api.typesafe.ai/v1/systemone"],
            seen);

        handler.Status = HttpStatusCode.InternalServerError;
        handler.Response = "busy";
        var providerError = new StringWriter();
        Assert.Equal(
            1,
            await JevPlayCommand.RunAsync(
                ["watch", "--route", "local", "--endpoint", "http://127.0.0.1:8099/v1/systemone", "--steps", "1"],
                usage,
                providerError,
                _ => http));
        Assert.Contains("Jev watch failed:", providerError.ToString(), StringComparison.Ordinal);
        Assert.Contains("500", providerError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_failures_stop_the_watch_without_printing_a_secret()
    {
        using var stallHttp = new HttpClient(new StallHandler());
        var stalled = new JevClient(
            JevEndpoint.Local(new Uri("http://127.0.0.1:8099/v1/systemone")),
            stallHttp,
            timeout: TimeSpan.FromMilliseconds(50));
        var timedOut = await Assert.ThrowsAsync<JevDecisionException>(() =>
            stalled.AskAsync("""{"tick":1}""", ["continue"], CancellationToken.None));
        Assert.Contains("timed out", timedOut.Message, StringComparison.Ordinal);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            stalled.AskAsync("""{"tick":1}""", ["continue"], cancelled.Token));

        using var brokenHttp = new HttpClient(new ThrowHandler());
        var broken = new JevClient(
            JevEndpoint.Local(new Uri("http://127.0.0.1:8099/v1/systemone")),
            brokenHttp);
        var transport = await Assert.ThrowsAsync<JevDecisionException>(() =>
            broken.AskAsync("""{"tick":1}""", ["continue"], CancellationToken.None));
        Assert.Contains("before a response", transport.Message, StringComparison.Ordinal);

        using var hugeHttp = new HttpClient(new HugeHandler());
        var huge = new JevClient(
            JevEndpoint.Local(new Uri("http://127.0.0.1:8099/v1/systemone")),
            hugeHttp);
        var declared = await Assert.ThrowsAsync<JevDecisionException>(() =>
            huge.AskAsync("""{"tick":1}""", ["continue"], CancellationToken.None));
        Assert.Contains("1048576", declared.Message, StringComparison.Ordinal);

        using var streamHttp = new HttpClient(new OverflowHandler());
        var overflowClient = new JevClient(
            JevEndpoint.Local(new Uri("http://127.0.0.1:8099/v1/systemone")),
            streamHttp);
        var overflow = await Assert.ThrowsAsync<JevDecisionException>(() =>
            overflowClient.AskAsync("""{"tick":1}""", ["continue"], CancellationToken.None));
        Assert.Contains("1048576", overflow.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owned_client_does_not_follow_a_redirect_or_forward_the_key()
    {
        // HttpListener accepted this connection on macOS and never completed the 302.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var hits = new List<string>();
        var authorization = new List<string?>();
        var server = new Thread(() => ServeRedirects(listener, hits, authorization, port))
        {
            IsBackground = true,
        };
        server.Start();

        try
        {
            using var http = new HttpClient(JevPlayCommand.CreateHandler());
            var client = new JevClient(
                new JevEndpoint(
                    JevProviderKind.OpenRouterDecisions,
                    new Uri($"http://127.0.0.1:{port}/v1/systemone"),
                    JevEndpoint.PinnedOpenRouterModel,
                    JevEndpoint.OpenRouterKeyVariable),
                http,
                static _ => "redirect-secret",
                TimeSpan.FromSeconds(5));
            var failed = await Assert.ThrowsAsync<JevDecisionException>(() =>
                client.AskAsync("""{"tick":1}""", ["continue"], CancellationToken.None));
            Assert.Contains("302", failed.Message, StringComparison.Ordinal);
            Assert.Contains("[redacted]", failed.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("redirect-secret", failed.Message, StringComparison.Ordinal);

            var output = new StringWriter();
            var error = new StringWriter();
            var code = await JevPlayCommand.RunAsync(
                ["watch", "--route", "local", "--endpoint", $"http://127.0.0.1:{port}/v1/systemone", "--steps", "1"],
                output,
                error);
            Assert.Equal(1, code);
            Assert.Contains("302", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer redirect-secret", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
            server.Join(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(["/v1/systemone", "/v1/systemone"], hits);
        Assert.Equal("Bearer redirect-secret", authorization[0]);
        Assert.Null(authorization[1]);
        Assert.DoesNotContain("/stolen", hits);
    }

    private static void ServeRedirects(
        TcpListener listener,
        List<string> hits,
        List<string?> authorization,
        int port)
    {
        while (hits.Count < 4)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (SocketException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            client.NoDelay = true;
            client.LingerState = new LingerOption(true, 0);
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 5000;
                stream.WriteTimeout = 5000;
                var header = ReadRequestHeader(stream);
                var path = RequestPath(header);
                var auth = RequestAuthorization(header);
                WriteRedirect(stream, port);
                hits.Add(path);
                authorization.Add(auth);
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                try
                {
                    client.Dispose();
                }
                catch (IOException)
                {
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private static string ReadRequestHeader(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var size = 0;
        while (size < buffer.Length)
        {
            var read = stream.Read(buffer, size, Math.Min(1024, buffer.Length - size));
            if (read <= 0)
            {
                break;
            }

            size += read;
            for (var index = 0; index <= size - 4; index++)
            {
                if (buffer[index] != '\r'
                    || buffer[index + 1] != '\n'
                    || buffer[index + 2] != '\r'
                    || buffer[index + 3] != '\n')
                {
                    continue;
                }

                return Encoding.ASCII.GetString(buffer, 0, index);
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, size);
    }

    private static string RequestPath(string header)
    {
        var lineEnd = header.IndexOf('\r', StringComparison.Ordinal);
        var line = lineEnd < 0 ? header : header[..lineEnd];
        var first = line.IndexOf(' ', StringComparison.Ordinal);
        if (first < 0 || first + 1 >= line.Length)
        {
            return string.Empty;
        }

        var second = line.IndexOf(' ', first + 1);
        return second < 0 ? line[(first + 1)..] : line[(first + 1)..second];
    }

    private static string? RequestAuthorization(string header)
    {
        foreach (var line in header.Split("\r\n"))
        {
            if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
            {
                return line["Authorization:".Length..].Trim();
            }
        }

        return null;
    }

    private static void WriteRedirect(NetworkStream stream, int port)
    {
        ReadOnlySpan<byte> payload = "rejected redirect-secret"u8;
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:"
            + port.ToString(CultureInfo.InvariantCulture)
            + "/stolen\r\nContent-Type: text/plain\r\nContent-Length: "
            + payload.Length.ToString(CultureInfo.InvariantCulture)
            + "\r\nConnection: close\r\n\r\n");
        stream.Write(head);
        stream.Write(payload);
        stream.Flush();
    }

    private sealed class ScriptHandler : HttpMessageHandler
    {
        public ScriptHandler(string response)
        {
            Response = response;
        }

        public string Response { get; set; }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string? Body { get; private set; }

        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Authorization = request.Headers.TryGetValues("Authorization", out var values)
                ? string.Join(' ', values)
                : null;
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StallHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The stalled request should have been cancelled.");
        }
    }

    private sealed class ThrowHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("down");
    }

    private sealed class HugeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1]),
            };
            response.Content.Headers.ContentLength = JevClient.MaximumResponseBytes + 1;
            return Task.FromResult(response);
        }
    }

    private sealed class OverflowHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new RepeatStream(JevClient.MaximumResponseBytes + 1)),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class RepeatStream : Stream
    {
        private int _remaining;

        public RepeatStream(int length)
        {
            _remaining = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Take(count);

        public override int Read(Span<byte> buffer) => Take(buffer.Length);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<int>(cancellationToken)
                : Task.FromResult(Take(count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<int>(cancellationToken)
                : new ValueTask<int>(Take(buffer.Length));

        private int Take(int count)
        {
            if (_remaining == 0 || count <= 0)
            {
                return 0;
            }

            var amount = Math.Min(count, _remaining);
            _remaining -= amount;
            return amount;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
