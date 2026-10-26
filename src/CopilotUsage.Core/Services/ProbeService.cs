using System.Text.Json;
using System.Text.RegularExpressions;
using CopilotUsage.Core.Authentication;
using CopilotUsage.Core.Models;
using CopilotUsage.Core.Providers;

namespace CopilotUsage.Core.Services;

public sealed record ProbeOptions(TimeSpan Timeout, bool SkipSdk = false,
    decimal? GitHubCycleCredits = null, string? GitHubCycleMonth = null);

public sealed record ProbeRun(DiagnosticReport Report, string? Login);

public sealed partial class ProbeService(GitHubTransport transport, IQuotaProvider sdk)
{
    public async Task<ProbeRun> RunAsync(GitHubCredentials credentials, ProbeOptions options, CancellationToken ct)
    {
        var observations = new List<Observation>();
        string? login = null;
        var identity = await CaptureAsync("github.user", async token =>
        {
            using var doc = await transport.GetAsync("user", token);
            UsageParser.RequireObject(doc.RootElement);
            var candidate = UsageParser.Text(doc.RootElement, "login");
            if (candidate is null || !LoginPattern().IsMatch(candidate))
                throw new ProbeException(ProbeStatus.InvalidData, "Compte GitHub absent ou invalide.");
            login = candidate;
            return new("github.user", ProbeStatus.Available, "Authentification GitHub.com réussie ; identité exclue du rapport.") { HttpStatus = 200 };
        }, options.Timeout, ct);
        observations.Add(identity);

        if (identity.Status != ProbeStatus.Available)
        {
            foreach (var source in new[] { SdkQuotaProvider.Source, UsageParser.InternalSource, "github.personal_billing.ai_credit" })
                observations.Add(new(source, ProbeStatus.Skipped, "Authentification GitHub.com non validée."));
            return new(new(DateTimeOffset.UtcNow, credentials.Method, observations, new(false, false, null, false)), null);
        }

        observations.Add(options.SkipSdk
            ? new(SdkQuotaProvider.Source, ProbeStatus.Skipped, "SDK désactivé explicitement pour ce diagnostic.")
            : await CaptureAsync(SdkQuotaProvider.Source, token => sdk.FetchAsync(credentials, token), options.Timeout, ct));

        async Task<Observation> ReadInternal(CancellationToken token)
        {
            using var doc = await transport.GetAsync("copilot_internal/user", token);
            return UsageParser.ParseInternal(doc.RootElement, DateTimeOffset.UtcNow);
        }
        var usage = await CaptureAsync(UsageParser.InternalSource, ReadInternal, options.Timeout, ct);
        observations.Add(usage);
        // A second fresh HTTP request proves refresh with the same credentials, without any interactive login.
        Observation? refresh = null;
        if (usage.Status == ProbeStatus.Available)
        {
            refresh = await CaptureAsync(UsageParser.InternalSource, ReadInternal, options.Timeout, ct);
            observations.Add(refresh with { Source = "github.copilot_internal/user.refresh" });
        }
        var now = DateTimeOffset.UtcNow;
        observations.Add(await CaptureAsync("github.personal_billing.ai_credit", async token =>
        {
            using var doc = await transport.GetAsync($"users/{Uri.EscapeDataString(login!)}/settings/billing/ai_credit/usage?year={now.Year}&month={now.Month}", token);
            return UsageParser.ParseBilling(doc.RootElement, now);
        }, options.Timeout, ct));

        bool monthly = usage is { Status: ProbeStatus.Available, Credits.CurrentMonthlyPeriod: true };
        bool automatic = monthly && refresh is { Status: ProbeStatus.Available, Credits.CurrentMonthlyPeriod: true } &&
            refresh.Credits.ResetAtUtc == usage.Credits!.ResetAtUtc && refresh.Credits.Consumed >= usage.Credits.Consumed;
        // A failed second read invalidates current display; keep earlier observation only as diagnostic evidence.
        bool? matches = null;
        if (automatic && options.GitHubCycleCredits is { } expected && options.GitHubCycleMonth == now.ToString("yyyy-MM"))
            matches = Math.Abs(refresh!.Credits!.Consumed - expected) <= 0.01m;
        var validation = new Validation(true, monthly && automatic, matches, automatic);
        return new(new(DateTimeOffset.UtcNow, credentials.Method, observations, validation), login);
    }

    public static async Task<Observation> CaptureAsync(string source, Func<CancellationToken, Task<Observation>> action,
        TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try { return await action(deadline.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(source, ProbeStatus.Timeout, "Délai dépassé ; données indisponibles."); }
        catch (ProbeException ex) { return new(source, ex.Status, ex.Message) { HttpStatus = ex.HttpStatus, RetryAtUtc = ex.RetryAtUtc }; }
        catch (HttpRequestException) { return new(source, ProbeStatus.NetworkError, "Connexion GitHub impossible ; données indisponibles."); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { return new(source, ProbeStatus.InvalidData, "Réponse ou configuration incompatible ; aucun contenu brut exporté."); }
        catch (Exception)
        {
            // SDK errors can embed auth headers, login or paths. Never export or print them.
            return new(source, ProbeStatus.Unavailable, "Source indisponible. Pour le SDK : vérifier runtime, token compatible et politique Copilot CLI. Détails sensibles supprimés.");
        }
    }

    public static DiagnosticReport MissingCredentials() => new(DateTimeOffset.UtcNow, "none",
        [new("github.user", ProbeStatus.MissingCredentials, "Définir COPILOT_USAGE_TOKEN dans le terminal local, ou utiliser gh auth login --hostname github.com.")],
        new(false, false, null, false));

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9-]{0,38}$", RegexOptions.CultureInvariant)]
    private static partial Regex LoginPattern();
}
