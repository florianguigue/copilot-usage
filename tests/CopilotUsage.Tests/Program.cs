using System.Text.Json;
using CopilotUsage.Core.Authentication;
using CopilotUsage.Core.Models;
using CopilotUsage.Core.Providers;
using CopilotUsage.Core.Services;
using GitHub.Copilot;

var failures = 0;
var passed = 0;
var now = DateTimeOffset.UtcNow;
var reset = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
string Payload(string quota, string? resetText = null, string extra = "") =>
    $$"""{"copilot_plan":"enterprise","quota_reset_date_utc":"{{resetText ?? reset.ToString("O")}}","quota_snapshots":{"premium_interactions":{{quota}}}{{extra}}}""";
const string valid = """{"token_based_billing":true,"credits_used":854.25,"entitlement":1500,"unlimited":false,"has_quota":true}""";
Observation Parse(string json) { using var doc = JsonDocument.Parse(json); return UsageParser.ParseInternal(doc.RootElement, now); }
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}"); }
}
async Task TestAsync(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}"); }
}

Test("fractional AI credits and calculated balance", () =>
{
    var result = Parse(Payload(valid));
    Check(result.Status == ProbeStatus.Available && result.Credits?.Consumed == 854.25m && result.Credits.RemainingCalculated == 645.75m);
});
Test("missing allocation is unknown", () =>
{
    var result = Parse(Payload("""{"token_based_billing":true,"credits_used":854.25}"""));
    Check(result.Credits is { Allocation: null, RemainingCalculated: null });
});
Test("zero allocation preserved and negative balance is not clamped", () =>
    Check(Parse(Payload(valid.Replace("1500", "0"))).Credits?.RemainingCalculated == -854.25m));
Test("missing consumption is never zero", () =>
    Check(Parse(Payload("""{"token_based_billing":true,"entitlement":1500}""")).Credits is null));
Test("real zero consumption is available", () =>
    Check(Parse(Payload(valid.Replace("854.25", "0"))).Credits?.Consumed == 0));
Test("Unix timestamps accepted for reset and source", () =>
{
    var json = Payload(valid.Replace("\"has_quota\":true", $"\"timestamp_utc\":{now.ToUnixTimeSeconds()}"))
        .Replace($"\"{reset:O}\"", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
    Check(Parse(json) is { Status: ProbeStatus.Available, Credits.Consumed: 854.25m });
});
Test("exhausted quota preserves actual consumption", () =>
    Check(Parse(Payload(valid.Replace("\"has_quota\":true", "\"has_quota\":false"))).Credits?.Consumed == 854.25m));
Test("premium requests are never AI credits", () =>
    Check(Parse(Payload(valid.Replace("true", "false"))).Credits is null));
Test("missing unit evidence is never AI credits", () =>
    Check(Parse(Payload("""{"credits_used":854,"entitlement":1500}""")).Credits is null));
Test("unlimited allocation has no calculated remainder", () =>
    Check(Parse(Payload(valid.Replace("\"unlimited\":false", "\"unlimited\":true"))).Credits?.RemainingCalculated is null));
Test("expired reset is stale", () =>
    Check(Parse(Payload(valid, now.AddDays(-1).ToString("O"))).Status == ProbeStatus.Stale));
Test("unknown period cannot qualify", () =>
    Check(!Parse(Payload(valid).Replace($"\"{reset:O}\"", "null")).Credits!.CurrentMonthlyPeriod));
Test("nonmonthly future reset cannot qualify", () =>
    Check(!Parse(Payload(valid, reset.AddDays(2).ToString("O"))).Credits!.CurrentMonthlyPeriod));
Test("stale source timestamp cannot qualify", () =>
    Check(Parse(Payload(valid.Replace("\"has_quota\":true", $"\"timestamp_utc\":\"{now.AddHours(-1):O}\""))).Status == ProbeStatus.Stale));
Test("future source timestamp cannot qualify", () =>
    Check(!Parse(Payload(valid.Replace("\"has_quota\":true", $"\"timestamp_utc\":\"{now.AddHours(1):O}\""))).Credits!.CurrentMonthlyPeriod));
foreach (var broken in new[] { "[]", Payload("[]"), Payload(valid.Replace("854.25", "-2")),
    Payload(valid.Replace("854.25", "\"854\"")), Payload(valid, "yesterday"), Payload(valid.Replace("false", "\"false\"")) })
    Test("invalid schema is rejected", () =>
    {
        try { Parse(broken); throw new Exception("Expected rejection"); }
        catch (ProbeException ex) { Check(ex.Status == ProbeStatus.InvalidData); }
    });
Test("privacy allowlist excludes credentials and arbitrary identity fields", () =>
{
    var result = Parse(Payload(valid, extra: ",\"login\":\"private-person\",\"token\":\"secret-value\",\"enterprise_list\":[{\"id\":42,\"name\":\"private-company\"}]"));
    var report = new DiagnosticReport(now, "environment:COPILOT_USAGE_TOKEN", [result], new(true, true, null, true));
    var text = JsonSerializer.Serialize(report, DiagnosticReport.JsonOptions);
    Check(!text.Contains("private-person") && !text.Contains("secret-value") && !text.Contains("private-company") && !text.Contains("enterprise_list"));
    Check(!new GitHubCredentials("secret-value", "test").ToString().Contains("secret-value"));
});
Test("human comparison and refresh are mandatory for eligibility", () =>
{
    Check(!new Validation(true, true, null, true).TechnicallyEligible);
    Check(!new Validation(true, true, false, true).TechnicallyEligible);
    Check(!new Validation(true, true, true, false).TechnicallyEligible);
    Check(new Validation(true, true, true, true).Decision == "AWAITING_USER_VALIDATION");
});
Test("personal billing empty is diagnostic, not enterprise zero", () =>
{
    using var doc = JsonDocument.Parse("""{"usageItems":[]}""");
    var result = UsageParser.ParseBilling(doc.RootElement, now);
    Check(result.Status == ProbeStatus.Partial && result.Credits is null && !result.Numbers.ContainsKey("grossQuantity.ai_credits"));
});
Test("personal billing isolates credits from requests and money", () =>
{
    using var doc = JsonDocument.Parse("""{"usageItems":[{"product":"Copilot","unitType":"requests","grossQuantity":999},{"product":"Copilot AI Credits","unitType":"ai-credits","grossQuantity":10,"netQuantity":3,"grossAmount":123}]}""");
    var result = UsageParser.ParseBilling(doc.RootElement, now);
    Check(result.Numbers["grossQuantity.ai_credits"] == 10 && result.Numbers["netQuantity.ai_credits"] == 3 && !result.Numbers.ContainsKey("grossAmount"));
});
await TestAsync("exception details never enter report", async () =>
{
    var result = await ProbeService.CaptureAsync("sdk", _ => throw new Exception("secret-value"), TimeSpan.FromSeconds(1), default);
    Check(!JsonSerializer.Serialize(result).Contains("secret-value"));
});

Test("euro estimate uses user formula with full decimal precision", () =>
{
    var reading = new CycleSnapshot(854.25m, reset, now, now);
    Check(reading.EstimatedEuros == 8.5425m && UsageFormatting.Euros(reading.EstimatedEuros) == "8,54 €");
    Check(UsageFormatting.Credits(0.001m) == "0,001");
});
Test("monitor keeps failure distinct from zero and recovers", () =>
{
    var good = Parse(Payload(valid));
    var state = UsageMonitor.Empty.Apply(good);
    Check(state.Current(now)?.Credits == 854.25m);
    state = state.Apply(new("test", ProbeStatus.Unauthorized, "401"));
    Check(state.Current(now) is null && state.LastKnown?.Credits == 854.25m);
    state = state.Apply(good with { Credits = good.Credits! with { Consumed = 0 } });
    Check(state.Current(now)?.Credits == 0 && state.Current(now)?.EstimatedEuros == 0);
});
Test("last known never implies fresh after restart", () =>
{
    var last = new CycleSnapshot(854.25m, reset, now, now);
    Check(new UsageMonitor(last, ProbeStatus.Stale, "cache").Current(now) is null);
});
Test("monitor expires after 15 minutes and at cycle rollover", () =>
{
    var last = new CycleSnapshot(854.25m, reset, now, now);
    var state = new UsageMonitor(last, ProbeStatus.Available, "");
    Check(state.Current(now.AddMinutes(16)) is null && state.Current(reset) is null);
});
Test("monitor rejects invalid or incomplete current-cycle evidence", () =>
{
    var observation = Parse(Payload(valid));
    Check(UsageMonitor.Empty.Apply(observation with { Credits = observation.Credits! with { CurrentMonthlyPeriod = false } }).Current(now) is null);
    Check(!new CycleSnapshot(-1, reset, now, now).IsValid);
    Check(!new CycleSnapshot(1, DateTimeOffset.MinValue, now, now).IsValid);
    Check(!new CycleSnapshot(1, reset, now.AddHours(1), now).IsFresh(now));
});
Test("compact tray labels remain bounded", () =>
{
    Check(UsageFormatting.Compact(854.25m) == "854");
    Check(UsageFormatting.Compact(1250) == "1,2k");
    Check(UsageFormatting.Compact(decimal.MaxValue).Length <= 4);
    Check(UsageFormatting.CompactEuros(8.5425m) == "8,54");
});

// A local Postman simulation exercises real HTTP, auth headers, routing, timeouts and classification.
if (Array.IndexOf(args, "--simulation") is var simulationIndex && simulationIndex >= 0 && simulationIndex + 1 < args.Length)
{
    var simulation = args[simulationIndex + 1].TrimEnd('/') + "/";
    if (!Uri.TryCreate(simulation, UriKind.Absolute, out var simUri) || !simUri.IsLoopback) throw new ArgumentException("Loopback only");
    var credentials = new GitHubCredentials("fake-local-test-token", "simulation");
    using var http = GitHubTransport.CreateClient(TimeSpan.FromSeconds(5));
    GitHubTransport Transport(string scenario) => new(http, credentials, new Uri(simulation + "github/" + scenario + "/"));
    await TestAsync("tray polling service reads current credits without diagnostic sources", async () =>
    {
        var result = await new CurrentCycleUsageService(Transport("success")).FetchAsync(default);
        Check(result.Status == ProbeStatus.Available && result.Credits?.Consumed == 854.25m);
    });
    foreach (var (scenario, status) in new[] { ("unauthorized", ProbeStatus.Unauthorized), ("forbidden", ProbeStatus.Forbidden),
        ("sso", ProbeStatus.SsoRequired), ("ratelimit", ProbeStatus.RateLimited), ("notfound", ProbeStatus.NotFound),
        ("servererror", ProbeStatus.Unavailable), ("html", ProbeStatus.InvalidData), ("redirect", ProbeStatus.Unavailable),
        ("oversized", ProbeStatus.InvalidData), ("malformed", ProbeStatus.InvalidData), ("slow", ProbeStatus.Timeout) })
        await TestAsync($"real HTTP {scenario}", async () =>
        {
            var result = await ProbeService.CaptureAsync("http", async ct =>
            {
                using var doc = await Transport(scenario).GetAsync("copilot_internal/user", ct);
                return UsageParser.ParseInternal(doc.RootElement, DateTimeOffset.UtcNow);
            }, TimeSpan.FromMilliseconds(400), default);
            Check(result.Status == status);
            if (scenario == "ratelimit") Check(result.RetryAtUtc.HasValue);
        });
    foreach (var scenario in new[] { "success", "noallocation", "missing", "legacy", "stale", "refreshfail", "decrease", "mismatch" })
        await TestAsync($"real service orchestration {scenario}", async () =>
        {
            var service = new ProbeService(Transport(scenario), new TestQuotaProvider());
            var expected = scenario == "mismatch" ? 999m : 854.25m;
            var run = await service.RunAsync(credentials, new(TimeSpan.FromSeconds(2), GitHubCycleCredits: expected,
                GitHubCycleMonth: DateTimeOffset.UtcNow.ToString("yyyy-MM")), default);
            Check(run.Report.Validation.Authenticated);
            Check(run.Report.Validation.TechnicallyEligible == (scenario is "success" or "noallocation"));
            Check(run.Report.Sources.Count == (scenario is "missing" or "legacy" or "stale" ? 4 : 5));
            var serialized = JsonSerializer.Serialize(run.Report, DiagnosticReport.JsonOptions);
            Check(!serialized.Contains("fake-local-test-token") && !serialized.Contains("simulation-user") && !serialized.Contains("hidden-secret"));
        });
    await TestAsync("unauthorized identity skips all providers", async () =>
    {
        var run = await new ProbeService(Transport("unauthorized"), new TestQuotaProvider()).RunAsync(credentials, new(TimeSpan.FromSeconds(2)), default);
        Check(!run.Report.Validation.Authenticated && run.Report.Sources.Skip(1).All(x => x.Status == ProbeStatus.Skipped));
    });
    await TestAsync("network failure is explicit", async () =>
    {
        var transport = new GitHubTransport(http, credentials, new Uri("http://127.0.0.1:1/"));
        var result = await ProbeService.CaptureAsync("http", async ct => { using var doc = await transport.GetAsync("user", ct); return new("http", ProbeStatus.Available, "unexpected"); }, TimeSpan.FromSeconds(2), default);
        Check(result.Status is ProbeStatus.NetworkError or ProbeStatus.Timeout);
    });
}
if (args.Contains("--sdk-smoke"))
    await TestAsync("official native SDK runtime starts and answers ping without inference", async () =>
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var environment = new Dictionary<string, string>
        {
            ["COPILOT_GITHUB_TELEMETRY_DISABLED"] = "true",
            ["OTEL_SDK_DISABLED"] = "true"
        };
        foreach (var name in new[] { "PATH", "SystemRoot", "USERPROFILE", "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        string? nativePath = Array.IndexOf(args, "--copilot-path") is var pathIndex && pathIndex >= 0 && pathIndex + 1 < args.Length
            ? args[pathIndex + 1] : null;
        await using var client = new CopilotClient(new CopilotClientOptions
        {
            Connection = RuntimeConnection.ForStdio(path: nativePath), Mode = CopilotClientMode.Empty,
            BaseDirectory = Path.GetFullPath("artifacts/sdk-smoke"), GitHubToken = "fake-local-test-token",
            UseLoggedInUser = false, LogLevel = CopilotLogLevel.None, Environment = environment
        });
        await client.StartAsync(deadline.Token);
        var ping = await client.PingAsync("probe-smoke", deadline.Token);
        Check(ping.Message == "pong: probe-smoke");
    });
Console.WriteLine($"\n{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;

sealed class TestQuotaProvider : IQuotaProvider
{
    public Task<Observation> FetchAsync(GitHubCredentials credentials, CancellationToken ct) =>
        Task.FromResult(new Observation(SdkQuotaProvider.Source, ProbeStatus.Skipped, "SDK non simulé : test de l'orchestration HTTP uniquement."));
}
