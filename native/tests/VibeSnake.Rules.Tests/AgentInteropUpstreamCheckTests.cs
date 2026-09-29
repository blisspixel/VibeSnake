using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class AgentInteropUpstreamCheckTests
{
    private static readonly byte[] Payload = "pin-body"u8.ToArray();

    [Fact]
    public void Probe_matches_injected_digests_and_reports_one_change()
    {
        var digest = Digest(Payload);
        var root = TempRoot();
        try
        {
            var calls = new List<string>();
            WriteBaseline(root, Pins(
                "https://example.invalid/spec",
                digest,
                "https://example.invalid/plugin",
                digest,
                "https://example.invalid/mcp",
                digest,
                extra: true));
            var matched = AgentInteropUpstreamCheck.Inspect(root, url =>
            {
                calls.Add(url);
                return Payload;
            });

            Assert.Null(matched.LoadError);
            Assert.Empty(matched.Errors);
            Assert.Equal(
                [
                    "https://example.invalid/spec",
                    "https://example.invalid/plugin",
                    "https://example.invalid/mcp",
                ],
                calls);

            WriteBaseline(root, Pins(
                "https://example.invalid/spec",
                new string('0', 64),
                "https://example.invalid/plugin",
                digest,
                "https://example.invalid/mcp",
                digest,
                extra: false));
            var changed = AgentInteropUpstreamCheck.Inspect(root, _ => Payload);
            Assert.Null(changed.LoadError);
            var error = Assert.Single(changed.Errors);
            Assert.Equal(
                "upstream specification digest changed: expected "
                + new string('0', 64)
                + ", got "
                + digest,
                error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Probe_reports_incomplete_pins_fetch_failures_and_cancellation()
    {
        var digest = Digest(Payload);
        var root = TempRoot();
        try
        {
            WriteBaseline(root, Pins(
                "http://example.invalid/spec",
                digest,
                "https://example.invalid/plugin",
                "ABC",
                "https://example.invalid/mcp",
                digest,
                extra: false));
            var mixed = AgentInteropUpstreamCheck.Inspect(root, url =>
            {
                if (url.Contains("mcp", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("line1\r\nline2");
                }

                return Payload;
            });
            Assert.Null(mixed.LoadError);
            Assert.Equal(3, mixed.Errors.Length);
            Assert.Equal("agent_plugins specification pin is incomplete", mixed.Errors[0]);
            Assert.Equal("agent_plugins plugin schema pin is incomplete", mixed.Errors[1]);
            Assert.Equal(
                "could not fetch mcp schema https://example.invalid/mcp: line1 line2",
                mixed.Errors[2]);
            Assert.DoesNotContain("\n", mixed.Errors[2], StringComparison.Ordinal);

            WriteBaseline(root, "{\"agent_plugins\":[]}");
            Assert.Equal(
                ["agent_plugins must be an object"],
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).Errors);
            WriteBaseline(root, "{\"other\":1}");
            Assert.Equal(
                ["agent_plugins must be an object"],
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).Errors);
            WriteBaseline(root, "{\"agent_plugins\":{\"spec_source_sha256\":" + Json(digest) + "}}");
            Assert.Contains(
                "agent_plugins specification pin is incomplete",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).Errors[0],
                StringComparison.Ordinal);
            WriteBaseline(root, "{\"agent_plugins\":{\"spec_source_url\":1,\"spec_source_sha256\":" + Json(digest) + "}}");
            Assert.Equal(
                "agent_plugins specification pin is incomplete",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).Errors[0]);
            WriteBaseline(root, "{\"agent_plugins\":{\"spec_source_url\":" + Json("https://example.invalid/spec") + ",\"spec_source_sha256\":1}}");
            Assert.Equal(
                "agent_plugins specification pin is incomplete",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).Errors[0]);

            WriteBaseline(root, Pins(
                "https://user:pass@example.invalid/spec",
                digest,
                "http://example.invalid/plugin",
                digest,
                "https://example.invalid/mcp",
                "ABC",
                extra: false));
            var handler = new RecordingHandler();
            using (var client = new UpstreamIntegrityClient(handler, TimeSpan.FromSeconds(5)))
            {
                var rejected = AgentInteropUpstreamCheck.Inspect(root, client.Get);
                Assert.Empty(handler.Requests);
                Assert.Contains("URL is not absolute https", rejected.Errors[0], StringComparison.Ordinal);
            }

            WriteBaseline(root, Pins(
                "https://example.invalid/spec",
                digest,
                "https://example.invalid/plugin",
                digest,
                "https://example.invalid/mcp",
                digest,
                extra: false));
            Assert.Throws<OperationCanceledException>(() =>
                AgentInteropUpstreamCheck.Inspect(root, _ => throw new OperationCanceledException()));
            Assert.Throws<ArgumentNullException>(() => AgentInteropUpstreamCheck.Inspect(null!, _ => Payload));
            Assert.Throws<ArgumentNullException>(() => AgentInteropUpstreamCheck.Inspect(root, null!));
            Assert.False(string.IsNullOrWhiteSpace(
                AgentInteropUpstreamCheck.Inspect("bad\0root", _ => Payload).LoadError));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Loader_rejects_malformed_oversized_linked_and_non_object_baselines()
    {
        var root = TempRoot();
        try
        {
            Assert.Equal(
                "interoperability baseline is missing",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);

            WriteBaseline(root, "{\"agent_plugins\":{},\"agent_plugins\":{}}");
            Assert.Equal(
                "duplicate JSON key: agent_plugins",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);
            WriteBaseline(root, "{\"agent_plugins\":{\"spec_source_url\":\"https://example.invalid/a\",\"spec_source_url\":\"https://example.invalid/b\"}}");
            Assert.Equal(
                "duplicate JSON key: spec_source_url",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);
            WriteBaseline(root, "{\"outer\":{\"name\":1},\"inner\":{\"name\":2},\"agent_plugins\":{}}");
            Assert.Null(AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);

            foreach (var json in new[] { "[]", "null", "\"x\"", "1", "true" })
            {
                WriteBaseline(root, json);
                Assert.Equal(
                    "the interoperability baseline root must be an object",
                    AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);
            }

            WriteBaseline(root, "{/*no*/}");
            Assert.Contains(
                "not valid JSON",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError,
                StringComparison.Ordinal);
            WriteBaseline(root, "{\"a\":1,}");
            Assert.Contains(
                "not valid JSON",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError,
                StringComparison.Ordinal);
            WriteBaseline(root, "{}{}");
            Assert.Contains(
                "not valid JSON",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError,
                StringComparison.Ordinal);

            var depth = AgentInteropUpstreamCheck.MaximumDepth + 1;
            var deep = new StringBuilder();
            for (var index = 0; index < depth; index++)
            {
                deep.Append("{\"a\":");
            }

            deep.Append('1');
            for (var index = 0; index < depth; index++)
            {
                deep.Append('}');
            }

            WriteBaseline(root, deep.ToString());
            Assert.Contains(
                "not valid JSON",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError,
                StringComparison.Ordinal);

            var path = BaselinePath(root);
            File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']);
            Assert.Equal(
                "interoperability baseline must be UTF-8 without a BOM",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);
            File.WriteAllBytes(path, [0xFF]);
            Assert.Equal(
                "interoperability baseline is not valid UTF-8",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);
            File.WriteAllBytes(path, new byte[AgentInteropUpstreamCheck.MaximumBaselineBytes + 1]);
            Assert.Equal(
                "interoperability baseline exceeds "
                + AgentInteropUpstreamCheck.MaximumBaselineBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " bytes",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);

            File.Delete(path);
            Directory.CreateDirectory(path);
            Assert.Equal(
                "interoperability baseline must be a regular file",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);
            Directory.Delete(path);

            WriteBaseline(root, "{}");
            var target = Path.Combine(root, "integrations", "baseline-target.json");
            File.Move(path, target);
            try
            {
                File.CreateSymbolicLink(path, target);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            Assert.Equal(
                "interoperability baseline must be a regular file",
                AgentInteropUpstreamCheck.Inspect(root, _ => Payload).LoadError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Checked_in_baseline_pins_are_complete_without_network()
    {
        var calls = new List<string>();
        var inspection = AgentInteropUpstreamCheck.Inspect(
            AgentInteropTestRepository.ResolveRepositoryRoot(),
            url =>
            {
                calls.Add(url);
                throw new InvalidOperationException("network is forbidden");
            });

        Assert.Null(inspection.LoadError);
        Assert.Equal(3, calls.Count);
        Assert.Equal(3, inspection.Errors.Length);
        Assert.All(inspection.Errors, error => Assert.Contains("network is forbidden", error, StringComparison.Ordinal));
        Assert.All(calls, url => Assert.StartsWith("https://", url, StringComparison.Ordinal));
    }

    [Fact]
    public void Command_keeps_upstream_probe_outside_combined_route()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(2, RepositoryCheckCommand.Run(["interop-upstream", " ", "extra"], output, error));
        Assert.Contains("RepositoryChecks interop-upstream [repository-root]", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());

        output = new StringWriter();
        error = new StringWriter();
        Assert.Equal(2, RepositoryCheckCommand.Run(["interop-upstream", "   "], output, error));
        Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);

        output = new StringWriter();
        error = new StringWriter();
        Assert.Equal(2, RepositoryCheckCommand.Run(["interop-upstream", "bad\0root"], output, error));
        Assert.Contains("Repository root is invalid.", error.ToString(), StringComparison.Ordinal);

        Assert.Throws<ArgumentNullException>(() => RepositoryCheckCommand.Run(
            ["interop-upstream"],
            output,
            error,
            (Func<string, byte[]>)null!));

        var root = TempRoot();
        try
        {
            var digest = Digest(Payload);
            WriteBaseline(root, Pins(
                "https://example.invalid/spec",
                digest,
                "https://example.invalid/plugin",
                digest,
                "https://example.invalid/mcp",
                digest,
                extra: false));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(["interop-upstream", root], output, error, _ => Payload));
            Assert.Equal(
                "Agent interoperability upstream specification and schema pins passed: "
                + Path.GetFullPath(root)
                + Environment.NewLine,
                output.ToString());
            Assert.Equal(string.Empty, error.ToString());

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                1,
                RepositoryCheckCommand.Run(["interop-upstream", root], output, error, _ => "other"u8.ToArray()));
            Assert.Equal(string.Empty, output.ToString());
            Assert.StartsWith(
                "Agent interoperability upstream check failed:" + Environment.NewLine + "  upstream specification",
                error.ToString(),
                StringComparison.Ordinal);

            File.Delete(BaselinePath(root));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(1, RepositoryCheckCommand.Run(["interop-upstream", root], output, error, _ => Payload));
            Assert.Equal(
                "Agent interoperability upstream check failed: interoperability baseline is missing" + Environment.NewLine,
                error.ToString());
            Assert.Equal(string.Empty, output.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Production_client_is_constructed_without_a_request_for_incomplete_pins()
    {
        var root = TempRoot();
        try
        {
            WriteBaseline(root, "{\"agent_plugins\":{}}");
            var output = new StringWriter();
            var error = new StringWriter();
            var code = RepositoryCheckCommand.Run(["interop-upstream", root], output, error);

            Assert.Equal(1, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("agent_plugins specification pin is incomplete", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("agent_plugins plugin schema pin is incomplete", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("agent_plugins mcp schema pin is incomplete", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("could not fetch", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Fetcher_enforces_https_status_timeout_and_request_headers()
    {
        Assert.Throws<ArgumentNullException>(() => new UpstreamIntegrityClient(null!, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UpstreamIntegrityClient(new RecordingHandler(), TimeSpan.Zero));

        using (var handler = UpstreamIntegrityClient.CreateHandler())
        {
            Assert.False(handler.AllowAutoRedirect);
            Assert.False(handler.UseCookies);
            Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
            Assert.Equal(UpstreamIntegrityClient.FetchTimeout, handler.ConnectTimeout);
            Assert.Equal(UpstreamIntegrityClient.MaximumResponseHeaderKilobytes, handler.MaxResponseHeadersLength);
        }

        var success = new RecordingHandler();
        success.Enqueue(() => ByteResponse(HttpStatusCode.OK, Payload));
        using (var client = new UpstreamIntegrityClient(success, TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(Payload, client.Get("https://example.invalid/spec#fragment"));
            var request = Assert.Single(success.Requests);
            Assert.Equal("https://example.invalid/spec", request.Uri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(UpstreamIntegrityClient.UserAgent, request.UserAgent);
            Assert.Equal(string.Empty, request.AcceptEncoding);
            Assert.Null(request.Authorization);
            Assert.False(request.HasContent);
        }

        foreach (var url in new[]
        {
            "http://example.invalid/spec",
            "https://",
            "https://user:pass@example.invalid/spec",
            "/relative",
            "   ",
        })
        {
            var blocked = new RecordingHandler();
            using var client = new UpstreamIntegrityClient(blocked, TimeSpan.FromSeconds(5));
            var exception = await Assert.ThrowsAsync<UpstreamFetchException>(() => client.GetAsync(url, CancellationToken.None));
            Assert.Equal("URL is not absolute https", exception.Message);
            Assert.Empty(blocked.Requests);
        }

        var missing = new RecordingHandler();
        missing.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK));
        using (var client = new UpstreamIntegrityClient(missing, TimeSpan.FromSeconds(5)))
        {
            Assert.Empty(client.Get("https://example.invalid/empty"));
        }

        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.InternalServerError, (HttpStatusCode)304 })
        {
            var failed = new RecordingHandler();
            failed.Enqueue(() => ByteResponse(status, Payload));
            using var client = new UpstreamIntegrityClient(failed, TimeSpan.FromSeconds(5));
            var exception = Assert.Throws<UpstreamFetchException>(() => client.Get("https://example.invalid/status"));
            Assert.Equal("HTTP " + ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message);
        }

        using (var client = new UpstreamIntegrityClient(new TimeoutHandler(), TimeSpan.FromMilliseconds(200)))
        {
            var exception = await Assert.ThrowsAsync<UpstreamFetchException>(() =>
                client.GetAsync("https://example.invalid/slow", CancellationToken.None));
            Assert.Equal("bounded timeout", exception.Message);
        }

        using (var client = new UpstreamIntegrityClient(new ThrowingHandler(new TimeoutException("connect stalled")), TimeSpan.FromSeconds(5)))
        {
            var exception = await Assert.ThrowsAsync<UpstreamFetchException>(() =>
                client.GetAsync("https://example.invalid/stall", CancellationToken.None));
            Assert.Equal("bounded timeout", exception.Message);
        }

        using (var client = new UpstreamIntegrityClient(new ThrowingHandler(new HttpRequestException("Name or service not known")), TimeSpan.FromSeconds(5)))
        {
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.GetAsync("https://example.invalid/dns", CancellationToken.None));
            Assert.Equal("Name or service not known", exception.Message);
        }

        var canceled = new RecordingHandler();
        using (var client = new UpstreamIntegrityClient(canceled, TimeSpan.FromSeconds(5)))
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.GetAsync("https://example.invalid/cancel", source.Token));
            Assert.Empty(canceled.Requests);
        }

        var disposed = new RecordingHandler();
        var owned = new UpstreamIntegrityClient(disposed, TimeSpan.FromSeconds(5));
        owned.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owned.Get("https://example.invalid/disposed"));
    }

    [Fact]
    public async Task Fetcher_follows_only_bounded_absolute_https_redirects()
    {
        foreach (var status in new[]
        {
            HttpStatusCode.MovedPermanently,
            HttpStatusCode.Redirect,
            HttpStatusCode.SeeOther,
            HttpStatusCode.TemporaryRedirect,
            HttpStatusCode.PermanentRedirect,
        })
        {
            var handler = new RecordingHandler();
            handler.Enqueue(() => Redirect(status, "https://example.invalid/final?x=1#section"));
            handler.Enqueue(() => ByteResponse(HttpStatusCode.OK, Payload));
            using var client = new UpstreamIntegrityClient(handler, TimeSpan.FromSeconds(5));
            Assert.Equal(Payload, await client.GetAsync("https://example.invalid/start", CancellationToken.None));
            Assert.Equal("https://example.invalid/final?x=1", handler.Requests[1].Uri);
            Assert.Equal(UpstreamIntegrityClient.UserAgent, handler.Requests[1].UserAgent);
            Assert.Null(handler.Requests[1].Authorization);
        }

        var two = new RecordingHandler();
        two.Enqueue(() => Redirect(HttpStatusCode.Redirect, "https://example.invalid/hop"));
        two.Enqueue(() => Redirect(HttpStatusCode.Redirect, "https://cdn.example/final"));
        two.Enqueue(() => ByteResponse(HttpStatusCode.OK, Payload));
        using (var client = new UpstreamIntegrityClient(two, TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(Payload, client.Get("https://example.invalid/start"));
            Assert.Equal(3, two.Requests.Count);
        }

        var exceeded = new RecordingHandler();
        exceeded.Enqueue(() => Redirect(HttpStatusCode.Redirect, "https://example.invalid/one"));
        exceeded.Enqueue(() => Redirect(HttpStatusCode.Redirect, "https://example.invalid/two"));
        exceeded.Enqueue(() => Redirect(HttpStatusCode.Redirect, "https://example.invalid/three"));
        using (var client = new UpstreamIntegrityClient(exceeded, TimeSpan.FromSeconds(5)))
        {
            var exception = Assert.Throws<UpstreamFetchException>(() => client.Get("https://example.invalid/start"));
            Assert.Equal(
                "exceeded " + UpstreamIntegrityClient.MaximumRedirects.ToString(System.Globalization.CultureInfo.InvariantCulture) + " redirects",
                exception.Message);
            Assert.Equal(3, exceeded.Requests.Count);
        }

        foreach (var location in new[]
        {
            "http://example.invalid/final",
            "/relative",
            "https://user:pass@example.invalid/final",
            "not a url",
        })
        {
            var handler = new RecordingHandler();
            handler.Enqueue(() => Redirect(HttpStatusCode.Redirect, location));
            using var client = new UpstreamIntegrityClient(handler, TimeSpan.FromSeconds(5));
            var exception = Assert.Throws<UpstreamFetchException>(() => client.Get("https://example.invalid/start"));
            Assert.Equal("redirect is not absolute https", exception.Message);
            Assert.Single(handler.Requests);
        }

        var missing = new RecordingHandler();
        missing.Enqueue(() => new HttpResponseMessage(HttpStatusCode.Redirect));
        using (var client = new UpstreamIntegrityClient(missing, TimeSpan.FromSeconds(5)))
        {
            var exception = Assert.Throws<UpstreamFetchException>(() => client.Get("https://example.invalid/start"));
            Assert.Equal("redirect is missing a location", exception.Message);
        }

        var blank = new RecordingHandler();
        blank.Enqueue(() => Redirect(HttpStatusCode.Redirect, "   "));
        using (var client = new UpstreamIntegrityClient(blank, TimeSpan.FromSeconds(5)))
        {
            var exception = Assert.Throws<UpstreamFetchException>(() => client.Get("https://example.invalid/start"));
            Assert.Equal("redirect is not absolute https", exception.Message);
        }

        var ambiguous = new RecordingHandler();
        ambiguous.Enqueue(() =>
        {
            var response = Redirect(HttpStatusCode.Redirect, "https://example.invalid/one");
            Assert.True(response.Headers.TryAddWithoutValidation("Location", "https://example.invalid/two"));
            return response;
        });
        using (var client = new UpstreamIntegrityClient(ambiguous, TimeSpan.FromSeconds(5)))
        {
            var exception = Assert.Throws<UpstreamFetchException>(() => client.Get("https://example.invalid/start"));
            Assert.Equal("redirect location is ambiguous", exception.Message);
        }

        var prefix = "https://example.com/";
        var accepted = prefix + new string('a', UpstreamIntegrityClient.MaximumLocationCharacters - prefix.Length);
        var allowed = new RecordingHandler();
        allowed.Enqueue(() => Redirect(HttpStatusCode.Redirect, accepted));
        allowed.Enqueue(() => ByteResponse(HttpStatusCode.OK, Payload));
        using (var client = new UpstreamIntegrityClient(allowed, TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(Payload, client.Get("https://example.invalid/start"));
            Assert.Equal(accepted, allowed.Requests[1].Uri);
        }

        var tooLong = prefix + new string('b', UpstreamIntegrityClient.MaximumLocationCharacters - prefix.Length + 1);
        var rejected = new RecordingHandler();
        rejected.Enqueue(() => Redirect(HttpStatusCode.Redirect, tooLong));
        using (var client = new UpstreamIntegrityClient(rejected, TimeSpan.FromSeconds(5)))
        {
            var exception = Assert.Throws<UpstreamFetchException>(() => client.Get("https://example.invalid/start"));
            Assert.Contains("redirect location exceeds", exception.Message, StringComparison.Ordinal);
            Assert.Single(rejected.Requests);
        }
    }

    [Fact]
    public async Task Fetcher_caps_response_bodies()
    {
        var limit = UpstreamIntegrityClient.MaximumResponseBytes;
        var exact = new byte[limit];
        exact[0] = 7;
        var exactHandler = new RecordingHandler();
        exactHandler.Enqueue(() => ByteResponse(HttpStatusCode.OK, exact));
        using (var client = new UpstreamIntegrityClient(exactHandler, TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(exact, await client.GetAsync("https://example.invalid/exact", CancellationToken.None));
        }

        var empty = new RecordingHandler();
        empty.Enqueue(() => ByteResponse(HttpStatusCode.OK, []));
        using (var client = new UpstreamIntegrityClient(empty, TimeSpan.FromSeconds(5)))
        {
            Assert.Empty(client.Get("https://example.invalid/empty"));
        }

        var advertised = new BodyStream(readableBytes: 0, reportedLength: limit + 1, seekable: true);
        var advertisedHandler = new RecordingHandler();
        advertisedHandler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(advertised),
        });
        using (var client = new UpstreamIntegrityClient(advertisedHandler, TimeSpan.FromSeconds(5)))
        {
            var exception = Assert.Throws<UpstreamFetchException>(() => client.Get("https://example.invalid/big"));
            Assert.Contains(limit.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
            Assert.False(advertised.WasRead);
        }

        var streamed = new BodyStream(readableBytes: limit + 1, reportedLength: null, seekable: false);
        var streamedHandler = new RecordingHandler();
        streamedHandler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(streamed),
        });
        using (var client = new UpstreamIntegrityClient(streamedHandler, TimeSpan.FromSeconds(5)))
        {
            var exception = await Assert.ThrowsAsync<UpstreamFetchException>(() =>
                client.GetAsync("https://example.invalid/stream", CancellationToken.None));
            Assert.Contains("response exceeds", exception.Message, StringComparison.Ordinal);
            Assert.True(streamed.WasRead);
        }

        var lying = new BodyStream(readableBytes: limit + 8, reportedLength: 4, seekable: true);
        var lyingHandler = new RecordingHandler();
        lyingHandler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(lying),
        });
        using (var client = new UpstreamIntegrityClient(lyingHandler, TimeSpan.FromSeconds(5)))
        {
            var exception = await Assert.ThrowsAsync<UpstreamFetchException>(() =>
                client.GetAsync("https://example.invalid/lie", CancellationToken.None));
            Assert.Contains("response exceeds", exception.Message, StringComparison.Ordinal);
            Assert.True(lying.WasRead);
        }
    }

    private static string Digest(byte[] payload) => Convert.ToHexStringLower(SHA256.HashData(payload));

    private static string Json(string value) => JsonSerializer.Serialize(value);

    private static string Pins(
        string specUrl,
        string specDigest,
        string pluginUrl,
        string pluginDigest,
        string mcpUrl,
        string mcpDigest,
        bool extra) =>
        "{\"note\":"
        + (extra ? "\"kept\"," : "null,")
        + "\"agent_plugins\":{"
        + "\"spec_source_url\":" + Json(specUrl) + ","
        + "\"spec_source_sha256\":" + Json(specDigest) + ","
        + "\"plugin_schema_url\":" + Json(pluginUrl) + ","
        + "\"plugin_schema_sha256\":" + Json(pluginDigest) + ","
        + "\"mcp_schema_url\":" + Json(mcpUrl) + ","
        + "\"mcp_schema_sha256\":" + Json(mcpDigest)
        + "}}";

    private static string TempRoot() => Directory.CreateTempSubdirectory("vibesnake-upstream-").FullName;

    private static string BaselinePath(string root) =>
        Path.Combine(root, "integrations", "agent-interop-baseline.json");

    private static void WriteBaseline(string root, string json)
    {
        var path = BaselinePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static HttpResponseMessage ByteResponse(HttpStatusCode status, byte[] body) =>
        new(status)
        {
            Content = new ByteArrayContent(body),
        };

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location)
    {
        var response = new HttpResponseMessage(status);
        Assert.True(response.Headers.TryAddWithoutValidation("Location", location));
        return response;
    }

    private sealed record RecordedRequest(
        string Uri,
        HttpMethod Method,
        string UserAgent,
        string AcceptEncoding,
        string? Authorization,
        bool HasContent);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _steps = new();

        internal List<RecordedRequest> Requests { get; } = [];

        internal void Enqueue(Func<HttpResponseMessage> step) =>
            _steps.Enqueue(step);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Method,
                request.Headers.UserAgent.ToString(),
                request.Headers.AcceptEncoding.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Content is not null));
            if (_steps.Count == 0)
            {
                throw new InvalidOperationException("unexpected request");
            }

            return Task.FromResult(_steps.Dequeue()());
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("delay should cancel");
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        internal ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_exception);
    }

    private sealed class BodyStream : Stream
    {
        private int _remaining;

        internal BodyStream(int readableBytes, long? reportedLength, bool seekable)
        {
            _remaining = readableBytes;
            ReportedLength = reportedLength;
            CanSeek = seekable;
        }

        internal bool WasRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek { get; }

        public override bool CanWrite => false;

        public override long Length => ReportedLength ?? throw new NotSupportedException();

        public override long Position { get; set; }

        private long? ReportedLength { get; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadBytes(count);

        public override int Read(Span<byte> buffer) => ReadBytes(buffer.Length);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<int>(cancellationToken)
                : Task.FromResult(ReadBytes(count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<int>(cancellationToken)
                : new ValueTask<int>(ReadBytes(buffer.Length));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int ReadBytes(int count)
        {
            WasRead = true;
            if (_remaining == 0 || count == 0)
            {
                return 0;
            }

            var read = Math.Min(count, _remaining);
            _remaining -= read;
            return read;
        }
    }
}
