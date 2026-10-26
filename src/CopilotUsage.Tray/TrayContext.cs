using CopilotUsage.Core.Authentication;
using CopilotUsage.Core.Models;
using CopilotUsage.Core.Providers;
using CopilotUsage.Core.Services;
using CopilotUsage.Tray.Settings;
using CopilotUsage.Tray.TrayIcon;
using CopilotUsage.Tray.Views;

namespace CopilotUsage.Tray;

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _notify = new();
    private readonly UsagePopup _popup = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly WindowsCredentialStore _credentials = new();
    private readonly LocalStore _store;
    private readonly Func<CancellationToken, Task<Observation>>? _fetchOverride;
    private readonly HttpClient _http = GitHubTransport.CreateClient(TimeSpan.FromSeconds(30));
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;
    private readonly ToolStripMenuItem _refreshMenu = new("Actualiser");
    private CancellationTokenSource? _refreshStop;
    private TraySettings _settings;
    private UsageMonitor _state;
    private DateTimeOffset _nextRefresh = DateTimeOffset.UtcNow;
    private bool _busy, _closing, _settingsOpen, _disposed;
    private long _generation;
    private string _iconKey = "";
    private Icon? _icon;
    private string? _localWarning;

    internal UsageMonitor State => _state;
    internal bool IsRefreshing => _busy;

    public TrayContext(bool startup, EventWaitHandle showEvent, LocalStore? store = null,
        Func<CancellationToken, Task<Observation>>? fetchOverride = null, bool visibleInTray = true)
    {
        _store = store ?? new LocalStore();
        _fetchOverride = fetchOverride;
        _showEvent = showEvent;
        _settings = _store.LoadSettings();
        _state = _store.LoadLastKnown() is { } previous
            ? new(previous, ProbeStatus.Stale, "Dernière lecture conservée ; synchronisation en attente.") : UsageMonitor.Empty;
        _menu.Items.Add("Ouvrir le récapitulatif", null, (_, _) => ShowPopup());
        _menu.Items.Add(_refreshMenu);
        _menu.Items.Add("Paramètres…", null, (_, _) => ShowSettings());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quitter", null, (_, _) => ExitThread());
        _menu.BackColor = Theme.Surface; _menu.ForeColor = Theme.Text;
        _refreshMenu.Click += async (_, _) => await RefreshAsync();
        _notify.ContextMenuStrip = _menu;
        _notify.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { if (_popup.Visible) _popup.Hide(); else ShowPopup(); } };
        _popup.RefreshRequested += async (_, _) => await RefreshAsync();
        _popup.SettingsRequested += (_, _) => ShowSettings();
        _ = _popup.Handle; // A hidden UI handle receives messages even when no form is visible.
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
        {
            if (!_closing && !_popup.IsDisposed)
            {
                try { _popup.BeginInvoke((Action)ShowPopup); }
                catch (InvalidOperationException) { }
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        Render();
        _notify.Visible = visibleInTray;
        _timer.Tick += async (_, _) =>
        {
            Render();
            if (!_busy && DateTimeOffset.UtcNow >= _nextRefresh) await RefreshAsync();
        };
        _timer.Start();
        _popup.BeginInvoke((Action)(async () => { if (!startup) ShowPopup(); await RefreshAsync(); }));
    }

    internal async Task RefreshAsync()
    {
        if (_busy || _closing || _state.RetryAtUtc > DateTimeOffset.UtcNow) return;
        _busy = true;
        var generation = _generation;
        var stop = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        _refreshStop = stop;
        Render();
        Observation result;
        try
        {
            if (_fetchOverride is not null) result = await _fetchOverride(stop.Token);
            else
            {
                var stored = _credentials.Read();
                var credentials = stored is not null ? new GitHubCredentials(stored, "windows-credential-manager")
                    : await GitHubCredentials.ResolveAsync(stop.Token);
                result = credentials is null
                    ? new(UsageParser.InternalSource, ProbeStatus.MissingCredentials, "Connexion requise.")
                    : await new CurrentCycleUsageService(new GitHubTransport(_http, credentials)).FetchAsync(stop.Token);
            }
        }
        catch (OperationCanceledException) { result = new(UsageParser.InternalSource, ProbeStatus.Timeout, "Synchronisation interrompue ou délai dépassé."); }
        catch (Exception) { result = new(UsageParser.InternalSource, ProbeStatus.Unavailable, "Connexion ou identifiants Windows indisponibles. Ouvre les paramètres."); }
        finally { _busy = false; _refreshStop = null; stop.Dispose(); }
        if (_closing || generation != _generation) return;
        _state = _state.Apply(result);
        _nextRefresh = DateTimeOffset.UtcNow.AddMinutes(_settings.RefreshMinutes);
        if (_state.RetryAtUtc > _nextRefresh) _nextRefresh = _state.RetryAtUtc.Value;
        if (_state.Current(DateTimeOffset.UtcNow) is { } current)
        {
            try { _store.SaveLastKnown(current); _localWarning = null; }
            catch (Exception) { _localWarning = "La dernière lecture ne peut pas être conservée sur ce PC."; }
        }
        Render();
    }

    private void Render()
    {
        if (_closing) return;
        var now = DateTimeOffset.UtcNow;
        var current = _state.Current(now);
        string iconText = current is null ? (_busy && _state.LastKnown is null ? "…" : "!")
            : _settings.IconMetric == IconMetric.Credits ? UsageFormatting.Compact(current.Credits) : UsageFormatting.CompactEuros(current.EstimatedEuros);
        var accent = current is null ? Theme.Amber : _settings.IconMetric == IconMetric.Credits ? Theme.Cyan : Theme.Mint;
        var iconKey = iconText + accent.ToArgb();
        if (_iconKey != iconKey)
        {
            var replacement = DynamicIcon.Create(iconText, accent);
            _notify.Icon = replacement;
            _icon?.Dispose(); _icon = replacement; _iconKey = iconKey;
        }
        var tooltip = current is null ? "Copilot · " + (_busy ? "actualisation…" : "indisponible")
            : $"Copilot · {UsageFormatting.Credits(current.Credits)} crédits · ≈ {UsageFormatting.Euros(current.EstimatedEuros)}";
        _notify.Text = tooltip.Length > 63 ? tooltip[..62] + "…" : tooltip;
        _refreshMenu.Enabled = !_busy && !(_state.RetryAtUtc > now);
        _popup.Present(_state, _busy, _nextRefresh, now);
    }

    private void ShowPopup()
    {
        if (_closing || _settingsOpen) return;
        Render(); _popup.ShowNearCursor();
        if (_localWarning is not null) _notify.Text = "Copilot · lecture OK, sauvegarde locale indisponible";
    }

    private void ShowSettings()
    {
        if (_settingsOpen || _closing) return;
        _settingsOpen = true; _popup.Hide();
        using var form = new SettingsForm(_settings, _store, _credentials, (token, ct) =>
            new CurrentCycleUsageService(new GitHubTransport(_http, new GitHubCredentials(token, "settings-validation"))).FetchAsync(ct));
        try
        {
            form.ShowDialog();
            if (form.SettingsSaved) { _settings = form.SavedSettings; _nextRefresh = DateTimeOffset.UtcNow.AddMinutes(_settings.RefreshMinutes); }
            if (form.ConnectionChanged)
            {
                _generation++; _refreshStop?.Cancel(); _state = UsageMonitor.Empty;
                try { _store.ClearLastKnown(); } catch (Exception) { }
                _nextRefresh = DateTimeOffset.UtcNow;
            }
        }
        finally { _settingsOpen = false; }
        ShowPopup();
        if (form.ConnectionChanged) _ = RefreshAsync();
    }

    protected override void ExitThreadCore()
    {
        _closing = true;
        _timer.Stop(); _refreshStop?.Cancel();
        _notify.Visible = false;
        _popup.AllowClose = true; _popup.Close();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _closing = true; _showWait.Unregister(null); _timer.Dispose();
            _refreshStop?.Cancel(); _notify.Visible = false; _notify.Dispose();
            _menu.Dispose(); _icon?.Dispose(); _popup.AllowClose = true; _popup.Dispose(); _http.Dispose();
        }
        base.Dispose(disposing);
    }
}
