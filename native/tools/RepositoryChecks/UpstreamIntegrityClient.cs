using System.Globalization;
using System.Net;

namespace RepositoryChecks;

internal sealed class UpstreamFetchException : Exception
{
    internal UpstreamFetchException(string message)
        : base(message)
    {
    }
}

internal sealed class UpstreamIntegrityClient : IDisposable
{
    internal const int MaximumResponseBytes = 1_048_576;

    internal const int MaximumRedirects = 2;

    internal const int MaximumLocationCharacters = 2_048;

    internal const int MaximumResponseHeaderKilobytes = 32;

    internal const string UserAgent = "VibeSnake-interop-drift/0.3";

    internal static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;

    private readonly TimeSpan _timeout;

    internal UpstreamIntegrityClient(HttpMessageHandler handler, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Fetch timeout must be positive.");
        }

        _timeout = timeout;
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    internal static SocketsHttpHandler CreateHandler() =>
        new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = FetchTimeout,
            MaxResponseHeadersLength = MaximumResponseHeaderKilobytes,
            UseCookies = false,
        };

    internal static UpstreamIntegrityClient Create() => new(CreateHandler(), FetchTimeout);

    public void Dispose() => _client.Dispose();

    internal byte[] Get(string url) =>
        GetAsync(url, CancellationToken.None).GetAwaiter().GetResult();

    internal async Task<byte[]> GetAsync(string url, CancellationToken cancellationToken)
    {
        if (!TryNormalizeHttps(url, out var current))
        {
            throw new UpstreamFetchException("URL is not absolute https");
        }

        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            for (var followed = 0; ; followed++)
            {
                linked.Token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                    .ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    if (followed >= MaximumRedirects)
                    {
                        throw new UpstreamFetchException(
                            "exceeded "
                            + MaximumRedirects.ToString(CultureInfo.InvariantCulture)
                            + " redirects");
                    }

                    current = RedirectTarget(response);
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new UpstreamFetchException(
                        "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                }

                if (response.Content is null)
                {
                    return [];
                }

                return await ReadBoundedAsync(response.Content, linked.Token).ConfigureAwait(false);
            }
        }
        catch (UpstreamFetchException)
        {
            throw;
        }
        catch (Exception exception) when (IsBoundedTimeout(exception, timeout, cancellationToken))
        {
            throw new UpstreamFetchException("bounded timeout");
        }
    }

    internal static bool TryNormalizeHttps(string? url, out Uri normalized)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || string.IsNullOrEmpty(parsed.IdnHost)
            || !string.IsNullOrEmpty(parsed.UserInfo))
        {
            normalized = null!;
            return false;
        }

        var builder = new UriBuilder(parsed)
        {
            Fragment = string.Empty,
            Scheme = Uri.UriSchemeHttps,
        };
        normalized = builder.Uri;
        return true;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static Uri RedirectTarget(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Location", out var values))
        {
            throw new UpstreamFetchException("redirect is missing a location");
        }

        var matches = 0;
        var location = string.Empty;
        foreach (var value in values)
        {
            matches++;
            location = value;
        }

        if (matches != 1)
        {
            throw new UpstreamFetchException("redirect location is ambiguous");
        }

        location = location.Trim();
        if (location.Length > MaximumLocationCharacters)
        {
            throw new UpstreamFetchException(
                "redirect location exceeds "
                + MaximumLocationCharacters.ToString(CultureInfo.InvariantCulture)
                + " characters");
        }

        if (!TryNormalizeHttps(location, out var next))
        {
            throw new UpstreamFetchException("redirect is not absolute https");
        }

        return next;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var advertised = content.Headers.ContentLength;
        if (advertised is long declared && declared > MaximumResponseBytes)
        {
            throw new UpstreamFetchException(TooLarge());
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var room = MaximumResponseBytes - (int)body.Length;
            if (room == 0)
            {
                var extra = new byte[1];
                var peeked = await stream.ReadAsync(extra.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (peeked > 0)
                {
                    throw new UpstreamFetchException(TooLarge());
                }

                break;
            }

            var read = await stream
                .ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, room)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            body.Write(buffer, 0, read);
        }

        return body.ToArray();
    }

    private static string TooLarge() =>
        "response exceeds " + MaximumResponseBytes.ToString(CultureInfo.InvariantCulture) + " bytes";

    private static bool IsBoundedTimeout(
        Exception exception,
        CancellationTokenSource timeout,
        CancellationToken caller)
    {
        if (caller.IsCancellationRequested)
        {
            return false;
        }

        if (exception is TimeoutException)
        {
            return true;
        }

        return timeout.IsCancellationRequested && exception is OperationCanceledException;
    }
}
