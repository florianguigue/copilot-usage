using CopilotUsage.Core.Models;
using CopilotUsage.Core.Providers;

namespace CopilotUsage.Core.Services;

// The validated credit source only: the tray doesn't run diagnostic billing/SDK calls every five minutes.
public sealed class CurrentCycleUsageService(GitHubTransport transport)
{
    public Task<Observation> FetchAsync(CancellationToken ct) => ProbeService.CaptureAsync(UsageParser.InternalSource, async token =>
    {
        using var response = await transport.GetAsync("copilot_internal/user", token);
        return UsageParser.ParseInternal(response.RootElement, DateTimeOffset.UtcNow);
    }, TimeSpan.FromSeconds(30), ct);
}
