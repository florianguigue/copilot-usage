using System.Text.Json;
using CopilotUsage.Core.Models;

namespace CopilotUsage.Tray.Settings;

internal enum IconMetric { Credits, Euros }
internal sealed record TraySettings(int RefreshMinutes = 5, IconMetric IconMetric = IconMetric.Credits)
{
    public bool IsValid => RefreshMinutes is >= 1 and <= 120 && Enum.IsDefined(IconMetric);
}

internal sealed class LocalStore(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopilotUsage");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public TraySettings LoadSettings() => Read<TraySettings>("settings.json") is { IsValid: true } settings ? settings : new();
    public CycleSnapshot? LoadLastKnown() => Read<CycleSnapshot>("last-usage.json") is { IsValid: true } snapshot ? snapshot : null;
    public void SaveSettings(TraySettings settings)
    {
        if (!settings.IsValid) throw new ArgumentException();
        Write("settings.json", settings);
    }
    public void SaveLastKnown(CycleSnapshot snapshot) => Write("last-usage.json", snapshot);
    public void ClearLastKnown() => File.Delete(Path.Combine(_directory, "last-usage.json"));

    private T? Read<T>(string name)
    {
        try
        {
            var path = Path.Combine(_directory, name);
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return default; }
    }

    private void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
