using System.Text.Json;
using CopilotUsage.Core.Models;
using CopilotUsage.Core.Authentication;
using CopilotUsage.Core.Providers;
using CopilotUsage.Core.Services;
using CopilotUsage.Tray.Settings;
using CopilotUsage.Tray.TrayIcon;
using CopilotUsage.Tray.Views;

namespace CopilotUsage.Tray;

// Explicit test mode: isolated files and a unique, fake Credential Manager entry, never real credentials.
internal static class WindowsSmoke
{
    public static int Run(string outputDirectory, string? simulation = null)
    {
        var results = new Dictionary<string, bool>();
        var errors = new Dictionary<string, string>();
        Directory.CreateDirectory(outputDirectory);
        void Check(string name, Func<bool> test)
        {
            try { results[name] = test(); }
            catch (Exception ex) { results[name] = false; errors[name] = ex.GetType().Name + (ex is System.ComponentModel.Win32Exception win32 ? $" ({win32.NativeErrorCode})" : "") + " · " + ex.StackTrace; }
        }
        Check("settings-and-last-value-roundtrip", () =>
        {
            var store = new LocalStore(Path.Combine(outputDirectory, "test-profile"));
            store.SaveSettings(new(7, IconMetric.Euros));
            if (store.LoadSettings() != new TraySettings(7, IconMetric.Euros)) return false;
            var now = DateTimeOffset.UtcNow;
            var reset = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
            var snapshot = new CycleSnapshot(854.25m, reset, now, now);
            store.SaveLastKnown(snapshot);
            var same = store.LoadLastKnown() == snapshot;
            store.ClearLastKnown();
            return same && store.LoadLastKnown() is null;
        });
        Check("credential-manager-roundtrip-fake-token-only", () =>
        {
            var store = new WindowsCredentialStore("CopilotUsage.Tests/" + Guid.NewGuid().ToString("N"));
            try
            {
                store.Write("fake-local-test-token");
                var correct = store.Read() == "fake-local-test-token";
                store.Delete();
                return correct && store.Read() is null;
            }
            finally { store.Delete(); }
        });
        Check("startup-command-quotes-executable", () => StartupRegistration.Command(@"C:\Example Folder\app.exe") == "\"C:\\Example Folder\\app.exe\" --startup");
        Check("dynamic-icons-create-and-dispose", () =>
        {
            foreach (var text in new[] { "0", "854", "1,2k", "!", "…" })
                for (var i = 0; i < 30; i++) { using var icon = DynamicIcon.Create(text, Theme.Cyan); if (icon.Handle == IntPtr.Zero) return false; }
            return true;
        });
        Check("popup-success-error-expired-zero", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var reset = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
            var last = new CycleSnapshot(854.25m, reset, now, now);
            using var popup = new UsagePopup { Opacity = 0, Location = new Point(-2000, -2000) };
            popup.Show();
            popup.Present(new(last, ProbeStatus.Available, ""), false, now.AddMinutes(5), now);
            var success = popup.CreditsText == UsageFormatting.Credits(854.25m) && popup.EurosText == UsageFormatting.Euros(8.5425m);
            SaveImage(popup, Path.Combine(outputDirectory, "popup-current.png"));
            popup.Present(new(last, ProbeStatus.NetworkError, ""), false, now.AddMinutes(5), now);
            var unavailable = popup.CreditsText == "—" && popup.EurosText == "—" && popup.StatusText.Contains("Indisponible");
            SaveImage(popup, Path.Combine(outputDirectory, "popup-unavailable.png"));
            popup.Present(new(last, ProbeStatus.Available, ""), false, now.AddMinutes(5), now.AddMinutes(16));
            var expired = popup.CreditsText == "—";
            popup.Present(new(last with { Credits = 0 }, ProbeStatus.Available, ""), false, now.AddMinutes(5), now);
            var zero = popup.CreditsText == "0" && popup.EurosText == UsageFormatting.Euros(0);
            popup.AllowClose = true; popup.Close();
            return success && unavailable && expired && zero;
        });
        Check("settings-form-renders-without-showing-secret", () =>
        {
            using var form = new SettingsForm(new(), new LocalStore(Path.Combine(outputDirectory, "settings-preview")),
                new WindowsCredentialStore("CopilotUsage.Tests/" + Guid.NewGuid().ToString("N")),
                (_, _) => Task.FromResult(new Observation("test", ProbeStatus.Unavailable, "test")))
            { Opacity = 0, Location = new Point(-2000, -2000), StartPosition = FormStartPosition.Manual };
            form.Show(); SaveImage(form, Path.Combine(outputDirectory, "settings.png")); form.Close();
            return true;
        });
        if (simulation is not null) Check("tray-context-real-http-success-401-recovery", () =>
        {
            var uri = new Uri(simulation.TrimEnd('/') + "/github/tray-refresh-" + Guid.NewGuid().ToString("N") + "/");
            if (uri.Scheme != "http" || !uri.IsLoopback) throw new ArgumentException("Local simulation only");
            using var http = GitHubTransport.CreateClient(TimeSpan.FromSeconds(5));
            var service = new CurrentCycleUsageService(new GitHubTransport(http, new GitHubCredentials("fake-local-test-token", "simulation"), uri));
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var context = new TrayContext(true, showEvent, new LocalStore(Path.Combine(outputDirectory, "context-profile")), service.FetchAsync, visibleInTray: false);
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            bool correct = false;
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    while (context.IsRefreshing || context.State.LastKnown is null) await Task.Delay(20, deadline.Token);
                    var initial = context.State.Current(DateTimeOffset.UtcNow);
                    if (initial?.Credits != 854.25m) return;
                    await context.RefreshAsync();
                    var failed = context.State.Status == ProbeStatus.Unauthorized && context.State.Current(DateTimeOffset.UtcNow) is null && context.State.LastKnown == initial;
                    await context.RefreshAsync();
                    correct = failed && context.State.Current(DateTimeOffset.UtcNow)?.EstimatedEuros == 8.5425m;
                }
                catch (Exception) { correct = false; }
                finally { context.ExitThread(); }
            };
            timer.Start(); Application.Run(context);
            return correct;
        });
        File.WriteAllText(Path.Combine(outputDirectory, "windows-smoke.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(outputDirectory, "windows-smoke-errors.json"), JsonSerializer.Serialize(errors, new JsonSerializerOptions { WriteIndented = true }));
        return results.Values.All(x => x) ? 0 : 1;
    }

    private static void SaveImage(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
