namespace CopilotUsage.Tray;

internal static class Program
{
    internal const string ShowEventName = @"Local\CopilotUsage.Tray.Show";
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--self-test")
            return WindowsSmoke.Run(args[1], args.Length == 4 && args[2] == "--simulation" ? args[3] : null);
        try
        {
            using var mutex = new Mutex(true, @"Local\CopilotUsage.Tray", out var created);
            if (!created)
            {
                try { using var existing = EventWaitHandle.OpenExisting(ShowEventName); existing.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            using var context = new TrayContext(args.Contains("--startup"), showEvent);
            Application.Run(context);
            mutex.ReleaseMutex();
            return 0;
        }
        catch (Exception)
        {
            MessageBox.Show("Copilot Usage n’a pas pu démarrer. Vérifie l’accès aux paramètres locaux et aux identifiants Windows.",
                "Copilot Usage", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
