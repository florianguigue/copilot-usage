using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using CopilotUsage.Core.Authentication;
using CopilotUsage.Core.Models;

namespace CopilotUsage.Core.Providers;

public sealed class ProbeException(ProbeStatus status, string message, int? httpStatus = null,
    DateTimeOffset? retry = null) : Exception(message)
{
    public ProbeStatus Status { get; } = status;
    public int? HttpStatus { get; } = httpStatus;
    public DateTimeOffset? RetryAtUtc { get; } = retry;
}

public sealed class GitHubTransport(HttpClient http, GitHubCredentials credentials, Uri? baseUri = null)
{
    private readonly Uri _baseUri = ValidateBase(baseUri ?? new("https://api.github.com/"));
    private static Uri ValidateBase(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !(uri.Scheme == "https" && uri.Host == "api.github.com" && uri.IsDefaultPort && uri.AbsolutePath == "/" ||
              uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException("Only api.github.com or a loopback test server is allowed.");
        return uri;
    }

    public static HttpClient CreateClient(TimeSpan timeout) => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = timeout,
        ActivityHeadersPropagator = System.Diagnostics.DistributedContextPropagator.CreateNoOutputPropagator()
    }) { Timeout = timeout };

    public async Task<JsonDocument> GetAsync(string relativePath, CancellationToken ct)
    {
        var target = new Uri(_baseUri, relativePath);
        if (target.Host != _baseUri.Host || target.Port != _baseUri.Port || target.Scheme != _baseUri.Scheme)
            throw new ArgumentException("Cross-host request refused.");
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Token);
        request.Headers.UserAgent.ParseAdd("CopilotUsage.Probe/0.1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw Failure(response);
        const int limit = 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new ProbeException(ProbeStatus.InvalidData, "Réponse trop volumineuse.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > limit) throw new ProbeException(ProbeStatus.InvalidData, "Réponse trop volumineuse.");
            buffer.Write(chunk, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static ProbeException Failure(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? retry = response.Headers.RetryAfter?.Date;
        if (response.Headers.RetryAfter?.Delta is { } delta) retry = now + delta;
        bool exhausted = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0");
        if (exhausted && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) &&
            long.TryParse(reset.FirstOrDefault(), CultureInfo.InvariantCulture, out var seconds) && seconds is >= 0 and <= 253402300799)
            retry = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (code == 429 || code == 403 && (exhausted || retry.HasValue))
            return new(ProbeStatus.RateLimited, "Limite GitHub atteinte ; aucune relance automatique immédiate.", code, retry);
        return code switch
        {
            401 => new(ProbeStatus.Unauthorized, "Authentification refusée ou expirée (401).", code),
            403 when response.Headers.Contains("X-GitHub-SSO") => new(ProbeStatus.SsoRequired, "Autorisation SSO requise (403).", code),
            403 => new(ProbeStatus.Forbidden, "Accès refusé (403) : permissions ou politique OAuth/entreprise à vérifier.", code),
            404 => new(ProbeStatus.NotFound, "Source absente ou non accessible (404).", code),
            >= 300 and < 400 => new(ProbeStatus.Unavailable, "Redirection refusée.", code),
            _ => new(ProbeStatus.Unavailable, "Service GitHub indisponible.", code)
        };
    }
}
