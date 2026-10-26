using System.Globalization;
using System.Text.Json;
using CopilotUsage.Core.Models;

namespace CopilotUsage.Core.Providers;

public static class UsageParser
{
    public const string InternalSource = "github.copilot_internal/user";

    public static Observation ParseInternal(JsonElement root, DateTimeOffset now)
    {
        RequireObject(root);
        var numbers = new Dictionary<string, decimal>();
        var flags = new Dictionary<string, bool>();
        var dates = new Dictionary<string, DateTimeOffset>();
        var plan = Text(root, "copilot_plan");
        plan = plan is "free" or "individual" or "individual_pro" or "individual_pro_plus" or "business" or "enterprise" ? plan : null;
        var reset = Date(root, "quota_reset_date_utc") ?? Date(root, "quota_reset_date");
        if (reset is { } resetValue) dates["quota_reset_date_utc"] = resetValue;
        CreditReading? credits = null;
        if (root.TryGetProperty("quota_snapshots", out var snapshots))
        {
            RequireObject(snapshots);
            foreach (var key in new[] { "premium_interactions", "chat", "completions" })
            {
                if (!snapshots.TryGetProperty(key, out var quota)) continue;
                RequireObject(quota);
                foreach (var field in new[] { "credits_used", "entitlement", "remaining", "percent_remaining", "overage_count" })
                    if (Number(quota, field) is { } n)
                    {
                        if (n < 0 && !(field == "entitlement" && n == -1)) Invalid();
                        if (field == "percent_remaining" && n > 100) Invalid();
                        numbers[$"{key}.{field}"] = n;
                    }
                foreach (var field in new[] { "token_based_billing", "has_quota", "unlimited" })
                    if (Boolean(quota, field) is { } b) flags[$"{key}.{field}"] = b;
                var sourceAt = Date(quota, "timestamp_utc");
                if (sourceAt is { } date) dates[$"{key}.timestamp_utc"] = date;
                if (key != "premium_interactions" || Boolean(quota, "token_based_billing") != true ||
                    Number(quota, "credits_used") is not { } consumed) continue;
                var allocation = Number(quota, "entitlement");
                var unlimited = Boolean(quota, "unlimited");
                if (allocation == -1)
                {
                    if (unlimited == false) Invalid();
                    allocation = null;
                }
                var utc = now.ToUniversalTime();
                var nextMonth = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
                // Monthly-cycle evidence comes from the returned reset, never from the local clock alone.
                var current = reset == nextMonth;
                if (sourceAt is { } observed && (observed > now.AddMinutes(5) || now - observed > TimeSpan.FromMinutes(15) ||
                    observed < nextMonth.AddMonths(-1))) current = false;
                credits = new(consumed, allocation, unlimited, reset, sourceAt, current);
            }
        }
        bool stale = reset <= now || credits?.SourceAtUtc is { } at && now - at > TimeSpan.FromMinutes(15);
        var status = stale ? ProbeStatus.Stale : credits?.CurrentMonthlyPeriod == true ? ProbeStatus.Available : ProbeStatus.Partial;
        return new(InternalSource, status, status switch
        {
            ProbeStatus.Stale => "Données périmées : aucun total courant validé.",
            ProbeStatus.Available => "AI Credits retournés ; cycle mensuel corroboré par la date de réinitialisation. Allocation ≠ budget individuel confirmé.",
            _ => "Réponse accessible ; AI Credits mensuels non confirmés (champs absents, ancien quota ou période inconnue)."
        }) { FetchedAtUtc = now, Numbers = numbers, Flags = flags, Dates = dates, Plan = plan, Credits = credits, HttpStatus = 200 };
    }

    public static Observation ParseBilling(JsonElement root, DateTimeOffset now)
    {
        RequireObject(root);
        if (!root.TryGetProperty("usageItems", out var items) || items.ValueKind != JsonValueKind.Array) Invalid();
        var numbers = new Dictionary<string, decimal>();
        var flags = new Dictionary<string, bool>();
        var count = 0;
        decimal gross = 0, net = 0;
        bool grossComplete = true, netComplete = true;
        foreach (var item in items.EnumerateArray())
        {
            RequireObject(item);
            if (Text(item, "unitType") is not ("ai-credits" or "credits") ||
                Text(item, "product") is not ("Copilot" or "Copilot AI Credits")) continue;
            count++;
            if (Number(item, "grossQuantity") is { } g && g >= 0) gross = checked(gross + g); else grossComplete = false;
            if (Number(item, "netQuantity") is { } n && n >= 0) net = checked(net + n); else netComplete = false;
        }
        numbers["recognized_credit_rows"] = count;
        if (count > 0 && grossComplete) numbers["grossQuantity.ai_credits"] = gross;
        if (count > 0 && netComplete) numbers["netQuantity.ai_credits"] = net;
        if (root.TryGetProperty("timePeriod", out var period))
        {
            RequireObject(period);
            foreach (var key in new[] { "year", "month" })
                if (Number(period, key) is { } value) numbers[$"timePeriod.{key}"] = value;
            flags["matches_requested_month"] = Number(period, "year") == now.Year && Number(period, "month") == now.Month;
        }
        return new("github.personal_billing.ai_credit", ProbeStatus.Partial,
            "Diagnostic uniquement : la facturation personnelle exclut la consommation financée par l'entreprise. Un résultat vide ne signifie pas zéro.")
        { FetchedAtUtc = now, HttpStatus = 200, Numbers = numbers, Flags = flags };
    }

    public static decimal? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
        Invalid(); return null;
    }
    public static bool? Boolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        Invalid(); return null;
    }
    public static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? Date(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParseExact(value.GetString(),
            ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)) return date;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) && seconds is >= 0 and <= 253402300799)
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        Invalid(); return null;
    }
    public static void RequireObject(JsonElement value) { if (value.ValueKind != JsonValueKind.Object) Invalid(); }
    private static void Invalid() => throw new ProbeException(ProbeStatus.InvalidData, "Format GitHub absent, modifié ou incohérent ; aucune valeur substituée par zéro.");
}
