using CopilotUsage.Core.Authentication;
using CopilotUsage.Core.Models;
using GitHub.Copilot;

namespace CopilotUsage.Core.Providers;

public interface IQuotaProvider
{
    Task<Observation> FetchAsync(GitHubCredentials credentials, CancellationToken ct);
}

public sealed class SdkQuotaProvider(string? cliPath = null) : IQuotaProvider
{
    public const string Source = "sdk.account.getQuota";

    // The generated account API is experimental in the pinned official SDK.
#pragma warning disable GHCP001
    public async Task<Observation> FetchAsync(GitHubCredentials credentials, CancellationToken ct)
    {
        var directory = Directory.CreateTempSubdirectory("CopilotUsage.Probe-");
        try
        {
            // SDK Environment replaces the inherited environment rather than overlaying it.
            var environment = new Dictionary<string, string>();
            foreach (var name in new[] { "PATH", "SystemRoot", "WINDIR", "USERPROFILE", "TEMP", "TMP", "APPDATA", "LOCALAPPDATA",
                     "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS" })
                if (System.Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
            environment["COPILOT_GITHUB_TELEMETRY_DISABLED"] = "true";
            environment["DO_NOT_TRACK"] = "1";
            environment["OTEL_SDK_DISABLED"] = "true";
            await using var client = new CopilotClient(new CopilotClientOptions
            {
                Connection = RuntimeConnection.ForStdio(path: cliPath),
                BaseDirectory = directory.FullName,
                Mode = CopilotClientMode.Empty,
                GitHubToken = credentials.Token,
                UseLoggedInUser = false,
                LogLevel = CopilotLogLevel.None,
                Environment = environment
            });
            await client.StartAsync(ct);
            // No session, prompt, tool or inference request is created.
            var result = await client.Rpc.Account.GetQuotaAsync(gitHubToken: credentials.Token, cancellationToken: ct);
            var numbers = new Dictionary<string, decimal>();
            var flags = new Dictionary<string, bool>();
            var dates = new Dictionary<string, DateTimeOffset>();
            foreach (var key in new[] { "premium_interactions", "chat", "completions" })
            {
                if (!result.QuotaSnapshots.TryGetValue(key, out var q)) continue;
                if (q.UsedRequests < 0 || q.EntitlementRequests < -1 || !double.IsFinite(q.RemainingPercentage) ||
                    q.RemainingPercentage is < 0 or > 100 || !double.IsFinite(q.Overage) || q.Overage < 0)
                    throw new ProbeException(ProbeStatus.InvalidData, "Quota SDK incohérent.");
                numbers[$"{key}.entitlementRequests"] = q.EntitlementRequests;
                numbers[$"{key}.usedRequests"] = q.UsedRequests;
                numbers[$"{key}.remainingPercentage"] = (decimal)q.RemainingPercentage;
                numbers[$"{key}.overage"] = (decimal)q.Overage;
                flags[$"{key}.isUnlimitedEntitlement"] = q.IsUnlimitedEntitlement;
                flags[$"{key}.overageAllowedWithExhaustedQuota"] = q.OverageAllowedWithExhaustedQuota;
                flags[$"{key}.usageAllowedWithExhaustedQuota"] = q.UsageAllowedWithExhaustedQuota;
                if (q.ResetDate is { } reset) dates[$"{key}.resetDate"] = reset;
            }
            return new(Source, numbers.Count > 0 ? ProbeStatus.Available : ProbeStatus.Partial,
                "Quotas d'interactions SDK ; aucune conversion en AI Credits mensuels.")
            { Numbers = numbers, Flags = flags, Dates = dates };
        }
        finally
        {
            // Delete only this invocation's verified child of the OS temporary directory.
            var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(directory.FullName);
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && directory.Name.StartsWith("CopilotUsage.Probe-", StringComparison.Ordinal))
            {
                try { directory.Delete(recursive: true); }
                catch (IOException) { /* A locked runtime file must not hide a valid diagnostic. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
#pragma warning restore GHCP001
}
