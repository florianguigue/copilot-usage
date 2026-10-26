using CopilotUsage.Core.Models;
using CopilotUsage.Tray.Settings;

namespace CopilotUsage.Tray.Views;

internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 120, Width = 95 };
    private readonly ComboBox _metric = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _startup = new() { Text = "Ouvrir à la connexion Windows", AutoSize = true, ForeColor = Theme.Text };
    private readonly TextBox _token = new() { UseSystemPasswordChar = true, MaxLength = 2560 };
    private readonly Label _feedback = Theme.Label("", 9, Theme.Muted);
    private readonly Button _save = Theme.Button("Enregistrer", primary: true);
    private readonly Button _forget = Theme.Button("Oublier le token");
    private readonly WindowsCredentialStore _credentials;
    private readonly LocalStore _store;
    private readonly Func<string, CancellationToken, Task<Observation>> _validateToken;
    private readonly CancellationTokenSource _stop = new();
    private readonly bool _startupWasEnabled;
    private bool _saving;
    private bool _disposed;
    public bool ConnectionChanged { get; private set; }
    public bool SettingsSaved { get; private set; }
    public TraySettings SavedSettings { get; private set; }

    public SettingsForm(TraySettings settings, LocalStore store, WindowsCredentialStore credentials,
        Func<string, CancellationToken, Task<Observation>> validateToken)
    {
        _credentials = credentials; _store = store; _validateToken = validateToken; SavedSettings = settings;
        Text = "Paramètres · Copilot Usage";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        ClientSize = new Size(460, 512);
        var title = Theme.Label("Paramètres", 20, Theme.Text, FontStyle.Bold);
        title.SetBounds(24, 18, 410, 40);
        AddLabel("Actualisation automatique", 24, 78);
        _interval.SetBounds(24, 108, 95, 30); _interval.Value = settings.RefreshMinutes;
        _interval.ForeColor = Color.FromArgb(20, 30, 45); _interval.BackColor = Color.FromArgb(240, 244, 248);
        _interval.AccessibleName = "Intervalle d’actualisation en minutes";
        var minutes = Theme.Label("minutes", 10, Theme.Muted); minutes.SetBounds(132, 111, 100, 27);
        AddLabel("Valeur dans l’icône", 249, 78);
        _metric.SetBounds(249, 108, 185, 30); _metric.Items.AddRange(["Crédits IA", "Euros estimés"]); _metric.SelectedIndex = (int)settings.IconMetric;
        _metric.ForeColor = Color.FromArgb(20, 30, 45); _metric.BackColor = Color.FromArgb(240, 244, 248);
        _metric.AccessibleName = "Valeur affichée dans l’icône";
        _startup.SetBounds(24, 161, 410, 28);
        try { _startupWasEnabled = StartupRegistration.IsEnabled(); _startup.Checked = _startupWasEnabled; }
        catch (Exception) { _startup.Enabled = false; }
        AddLabel("Token GitHub.com", 24, 218);
        _token.SetBounds(24, 248, 410, 30);
        _token.ForeColor = Color.FromArgb(20, 30, 45); _token.BackColor = Color.FromArgb(240, 244, 248);
        _token.AccessibleName = "Token GitHub.com, saisie masquée";
        _token.PlaceholderText = "Laisser vide pour conserver la connexion";
        var privacy = Theme.Label("Saisie masquée. Le token est testé avant d’être mémorisé\ndans le Gestionnaire d’identifiants Windows.", 9, Theme.Muted);
        privacy.SetBounds(24, 287, 410, 43);
        _forget.SetBounds(24, 343, 180, 34);
        try
        {
            var stored = _credentials.Read() is not null;
            _forget.Enabled = stored;
            _feedback.Text = stored ? "Un token est mémorisé dans Windows." : "Aucun token mémorisé. Variable locale ou GitHub CLI possibles.";
        }
        catch (Exception) { _feedback.Text = "Gestionnaire d’identifiants Windows inaccessible."; }
        _feedback.SetBounds(24, 391, 410, 52);
        _save.SetBounds(249, 456, 185, 36);
        var cancel = Theme.Button("Fermer"); cancel.SetBounds(24, 456, 185, 36); cancel.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        _forget.Click += (_, _) => ForgetToken();
        Controls.AddRange([title, _interval, minutes, _metric, _startup, _token, privacy, _forget, _feedback, _save, cancel]);
        AcceptButton = _save;
        FormClosing += (_, _) => { _stop.Cancel(); _token.Clear(); };
    }

    private void AddLabel(string text, int x, int y)
    { var label = Theme.Label(text, 10, Theme.Text, FontStyle.Bold); label.SetBounds(x, y, 210, 26); Controls.Add(label); }

    private async Task SaveAsync()
    {
        if (_saving) return;
        _saving = true; _save.Enabled = false; _forget.Enabled = false; _token.Enabled = false;
        var token = _token.Text.Trim();
        try
        {
            if (token.Length > 0)
            {
                _feedback.Text = "Vérification des crédits avec ce token…";
                var result = await _validateToken(token, _stop.Token);
                if (IsDisposed || _stop.IsCancellationRequested) return;
                if (result.Status != ProbeStatus.Available || result.Credits?.CurrentMonthlyPeriod != true)
                { _feedback.Text = UsagePopup.ShortMessage(result.Status, "Token ou données non validés. Le token existant est conservé."); return; }
            }
            var settings = new TraySettings((int)_interval.Value, (IconMetric)_metric.SelectedIndex);
            // Persist nonsecret settings first; a failed disk operation must not replace a working token.
            _store.SaveSettings(settings);
            if (_startup.Enabled && _startup.Checked != _startupWasEnabled) StartupRegistration.SetEnabled(_startup.Checked);
            if (token.Length > 0) { _credentials.Write(token); ConnectionChanged = true; }
            _token.Clear(); SavedSettings = settings; SettingsSaved = true;
            DialogResult = DialogResult.OK;
            _saving = false; Close();
        }
        catch (OperationCanceledException) { if (!IsDisposed) _feedback.Text = "Vérification annulée."; }
        catch (Exception) { if (!IsDisposed) _feedback.Text = "Enregistrement impossible. Vérifie l’accès aux paramètres et aux identifiants Windows."; }
        finally { _saving = false; if (!IsDisposed) { _save.Enabled = true; _token.Enabled = true; try { _forget.Enabled = _credentials.Read() is not null; } catch (Exception) { _forget.Enabled = false; } } }
    }

    private void ForgetToken()
    {
        try { _credentials.Delete(); _token.Clear(); ConnectionChanged = true; _forget.Enabled = false; _feedback.Text = "Token Windows supprimé. La variable locale ou gh reste utilisable."; }
        catch (Exception) { _feedback.Text = "Suppression impossible dans les identifiants Windows."; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _stop.Cancel(); _stop.Dispose(); }
        base.Dispose(disposing);
    }
}
