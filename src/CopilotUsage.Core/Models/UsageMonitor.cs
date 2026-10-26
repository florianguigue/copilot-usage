using System.Globalization;

namespace CopilotUsage.Core.Models;

public sealed record CycleSnapshot(decimal Credits, DateTimeOffset ResetAtUtc, DateTimeOffset FetchedAtUtc, DateTimeOffset? SourceAtUtc)
{
    // User-defined estimate. No FX conversion and no assumption about a GitHub invoice.
    public decimal EstimatedEuros => Credits / 100m;
    public DateTimeOffset CycleStartUtc => ResetAtUtc.AddMonths(-1);

    public bool IsValid => Credits >= 0 && ResetAtUtc.Year > 1 && ResetAtUtc.Offset == TimeSpan.Zero && ResetAtUtc.Day == 1 &&
        ResetAtUtc.TimeOfDay == TimeSpan.Zero && FetchedAtUtc >= CycleStartUtc && FetchedAtUtc < ResetAtUtc;

    public bool IsFresh(DateTimeOffset now) => IsValid && now >= CycleStartUtc && now < ResetAtUtc &&
        FetchedAtUtc <= now.AddMinutes(5) && now - FetchedAtUtc <= TimeSpan.FromMinutes(15) &&
        (SourceAtUtc is not { } source || source >= CycleStartUtc && source <= now.AddMinutes(5) && now - source <= TimeSpan.FromMinutes(15));
}

public sealed record UsageMonitor(CycleSnapshot? LastKnown, ProbeStatus Status, string Message, DateTimeOffset? RetryAtUtc = null)
{
    public static UsageMonitor Empty { get; } = new(null, ProbeStatus.Unavailable, "Première synchronisation en attente.");
    public CycleSnapshot? Current(DateTimeOffset now) => Status == ProbeStatus.Available && LastKnown?.IsFresh(now) == true ? LastKnown : null;

    public UsageMonitor Apply(Observation result)
    {
        if (result is { Status: ProbeStatus.Available, Credits: { CurrentMonthlyPeriod: true, ResetAtUtc: { } reset } credits })
        {
            var snapshot = new CycleSnapshot(credits.Consumed, reset.ToUniversalTime(), result.FetchedAtUtc, credits.SourceAtUtc);
            if (snapshot.IsFresh(result.FetchedAtUtc)) return new(snapshot, ProbeStatus.Available, "Synchronisé avec GitHub.");
        }
        return new(LastKnown, result.Status == ProbeStatus.Available ? ProbeStatus.InvalidData : result.Status,
            result.Status == ProbeStatus.Available ? "Cycle courant non confirmé." : result.Message, result.RetryAtUtc);
    }
}

public static class UsageFormatting
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    public static string Credits(decimal credits) => credits.ToString("#,0.############################", French);
    public static string Euros(decimal euros) => euros.ToString("N2", French) + " €";
    public static string CompactEuros(decimal euros) => euros < 10 ? euros.ToString("0.##", French)
        : euros < 100 ? euros.ToString("0.#", French) : Compact(euros);
    public static string Compact(decimal value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value < 1000) return decimal.Truncate(value).ToString("0", French);
        foreach (var (scale, suffix) in new[] { (1_000_000_000m, "G"), (1_000_000m, "M"), (1_000m, "k") })
            if (value >= scale)
            {
                var scaled = value / scale;
                if (scaled >= 1000) return "999+";
                return (decimal.Truncate(scaled * (scaled < 10 ? 10 : 1)) / (scaled < 10 ? 10 : 1)).ToString("0.#", French) + suffix;
            }
        return "0";
    }
}
