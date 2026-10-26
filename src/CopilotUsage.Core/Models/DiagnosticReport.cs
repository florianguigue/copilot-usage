using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotUsage.Core.Models;

public enum ProbeStatus { Available, Partial, MissingCredentials, Unauthorized, Forbidden, SsoRequired, RateLimited, NotFound, InvalidData, Stale, NetworkError, Timeout, Unavailable, Skipped }

// Only allowlisted, typed data is exported. Never add a raw payload, exception or identity here.
public sealed record Observation(string Source, ProbeStatus Status, string Message)
{
    public DateTimeOffset FetchedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public int? HttpStatus { get; init; }
    public DateTimeOffset? RetryAtUtc { get; init; }
    public Dictionary<string, decimal> Numbers { get; init; } = [];
    public Dictionary<string, bool> Flags { get; init; } = [];
    public Dictionary<string, DateTimeOffset> Dates { get; init; } = [];
    public string? Plan { get; init; }
    public CreditReading? Credits { get; init; }
}

public sealed record CreditReading(decimal Consumed, decimal? Allocation, bool? Unlimited,
    DateTimeOffset? ResetAtUtc, DateTimeOffset? SourceAtUtc, bool CurrentMonthlyPeriod)
{
    public string Unit => "ai_credits";
    // Calculation is explicit; entitlement is not asserted to be an employer's individual budget.
    public decimal? RemainingCalculated => Unlimited == false && Allocation is { } a ? a - Consumed : null;
}

public sealed record Validation(bool Authenticated, bool MonthlyAiCredits, bool? MatchesGitHub,
    bool AutomaticRefresh, bool ExplicitErrorHandling = true)
{
    public bool TechnicallyEligible => Authenticated && MonthlyAiCredits && MatchesGitHub == true && AutomaticRefresh;
    public string Decision => TechnicallyEligible ? "AWAITING_USER_VALIDATION" : "HOLD";
    public string Stage2 => "SUSPENDED_UNTIL_EXPLICIT_USER_VALIDATION";
}

public sealed record DiagnosticReport(DateTimeOffset GeneratedAtUtc, string AuthenticationMethod,
    List<Observation> Sources, Validation Validation)
{
    public int SchemaVersion => 1;
    public string Privacy => "No account login, account ID, organization, raw response, token or exception is exported.";
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
