using System.Globalization;
using CopilotUsage.Core.Models;

namespace CopilotUsage.Tray.Views;

internal sealed class UsagePopup : Form
{
    private readonly Label _period = Theme.Label("Cycle en cours", 10, Theme.Muted);
    private readonly Label _status = Theme.Label("En attente", 9, Theme.Amber, FontStyle.Bold);
    private readonly MetricCard _credits = new("CONSOMMATION DU CYCLE", "AI Credits", Theme.Cyan);
    private readonly MetricCard _euros = new("COÛT ESTIMÉ", "100 crédits IA = 1 €", Theme.Mint);
    private readonly Label _sync = Theme.Label("", 9, Theme.Muted);
    private readonly Label _detail = Theme.Label("", 9, Theme.Muted);
    private readonly Button _refresh = Theme.Button("Actualiser", primary: true);
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public string CreditsText => _credits.Value.Text;
    public string EurosText => _euros.Value.Text;
    public string StatusText => _status.Text;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool AllowClose { get; set; }

    public UsagePopup()
    {
        Text = "Copilot Usage";
        AccessibleName = "Consommation Copilot";
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(410, 574);
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.Manual;
        var title = Theme.Label("Copilot Usage", 19, Theme.Text, FontStyle.Bold);
        title.SetBounds(22, 18, 270, 38);
        var close = Theme.Button("×");
        close.Font = new Font("Segoe UI", 15);
        close.SetBounds(353, 18, 34, 34);
        close.AccessibleName = "Masquer le panneau";
        close.Click += (_, _) => Hide();
        _period.SetBounds(24, 62, 350, 24);
        _status.SetBounds(24, 96, 350, 24);
        _credits.SetBounds(22, 132, 366, 144);
        _euros.SetBounds(22, 288, 366, 144);
        _sync.SetBounds(24, 447, 362, 25);
        _detail.SetBounds(24, 477, 362, 43);
        _refresh.SetBounds(22, 527, 177, 36);
        var settings = Theme.Button("Paramètres");
        settings.SetBounds(211, 527, 177, 36);
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        Controls.AddRange([title, close, _period, _status, _credits, _euros, _sync, _detail, _refresh, settings]);
        Deactivate += (_, _) => Hide();
        FormClosing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
    }

    public void Present(UsageMonitor state, bool refreshing, DateTimeOffset nextRefresh, DateTimeOffset now)
    {
        var current = state.Current(now);
        _credits.SetValue(current is null ? "—" : UsageFormatting.Credits(current.Credits));
        _euros.SetValue(current is null ? "—" : UsageFormatting.Euros(current.EstimatedEuros));
        _period.Text = current is null ? "Cycle en cours · lecture indisponible" : Period(current);
        _status.Text = refreshing ? "●  Actualisation…" : current is not null ? "●  À jour" : "●  Indisponible";
        _status.ForeColor = current is not null ? Theme.Mint : Theme.Amber;
        _refresh.Enabled = !refreshing && !(state.RetryAtUtc > now);
        _refresh.Text = refreshing ? "Actualisation…" : "Actualiser";
        _sync.Text = current is not null
            ? $"Synchronisé à {current.FetchedAtUtc.ToLocalTime():HH:mm:ss} · prochaine lecture à {nextRefresh.ToLocalTime():HH:mm}"
            : state.Status == ProbeStatus.Available ? "La dernière lecture a expiré. Actualisation en attente." : ShortMessage(state.Status, state.Message);
        if (current is not null) _detail.Text = "Estimation selon ta formule crédits ÷ 100.\nLe montant est arrondi au centime.";
        else if (state.LastKnown is { } last)
            _detail.Text = $"Dernière lecture ({last.CycleStartUtc:MM/yyyy}, {last.FetchedAtUtc.ToLocalTime():dd/MM HH:mm}) :\n{UsageFormatting.Credits(last.Credits)} crédits · {UsageFormatting.Euros(last.EstimatedEuros)} estimés";
        else _detail.Text = state.Status is ProbeStatus.MissingCredentials or ProbeStatus.Unauthorized
            ? "Ajoute le token validé dans les paramètres.\nIl sera conservé dans les identifiants Windows."
            : "Aucune lecture disponible pour ce cycle.\nUne nouvelle tentative est programmée.";
        if (state.RetryAtUtc > now) _sync.Text = $"Limite GitHub · reprise possible à {state.RetryAtUtc.Value.ToLocalTime():HH:mm:ss}";
    }

    public void ShowNearCursor()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(Math.Clamp(Cursor.Position.X - Width / 2, area.Left, Math.Max(area.Left, area.Right - Width - 12)),
            Math.Clamp(Cursor.Position.Y - Height - 12, area.Top, Math.Max(area.Top, area.Bottom - Height - 12)));
        Show(); Activate();
    }

    internal static string Period(CycleSnapshot snapshot)
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        return $"Cycle du {snapshot.CycleStartUtc.Day} au {snapshot.ResetAtUtc.AddDays(-1).Day} {snapshot.CycleStartUtc.ToString("MMMM yyyy", culture)} · UTC";
    }

    internal static string ShortMessage(ProbeStatus status, string fallback) => status switch
    {
        ProbeStatus.MissingCredentials => "Connexion requise · ouvre les paramètres.",
        ProbeStatus.Unauthorized => "Token refusé ou expiré · ouvre les paramètres.",
        ProbeStatus.Forbidden => "Accès GitHub refusé · permissions à vérifier.",
        ProbeStatus.SsoRequired => "Autorisation SSO GitHub requise.",
        ProbeStatus.NetworkError => "Connexion impossible · nouvelle tentative programmée.",
        ProbeStatus.Timeout => "GitHub n’a pas répondu dans le délai prévu.",
        ProbeStatus.Stale => "GitHub a retourné des données périmées.",
        ProbeStatus.Partial or ProbeStatus.InvalidData => "Crédits du cycle absents ou format incompatible.",
        _ => fallback
    };
}
