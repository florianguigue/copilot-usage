using System.Globalization;
using System.Text;
using System.Text.Json;
using CopilotUsage.Core.Authentication;
using CopilotUsage.Core.Models;
using CopilotUsage.Core.Providers;
using CopilotUsage.Core.Services;

Console.OutputEncoding = Encoding.UTF8;
return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    var output = "diagnostic.json";
    var timeout = 30;
    bool skipSdk = false, login = false;
    decimal? expected = null;
    string? month = null, cliPath = null;
    try
    {
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException();
            switch (args[i])
            {
                case "--help" or "-h":
                    Console.WriteLine("""
                        Copilot Usage Probe — étape 1 uniquement
                        --output <fichier.json>          Rapport sans identité ni secret (défaut : diagnostic.json)
                        --timeout <secondes>            Délai par source, de 1 à 120 (défaut : 30)
                        --skip-sdk                      Diagnostic HTTP seul (ne valide pas le SDK)
                        --copilot-path <exécutable>      Runtime Copilot optionnel ; runtime embarqué par défaut
                        --github-cycle-credits <nombre> Valeur AI Credits lue sur GitHub.com, point décimal
                        --github-cycle-month <AAAA-MM>  Mois affiché sur GitHub.com ; requis avec la valeur
                        --login                         Connexion interactive via gh, jamais automatique

                        Auth : COPILOT_USAGE_TOKEN, sinon gh auth token --hostname github.com.
                        Aucun token en argument. Aucun prompt Copilot ni consommation de modèle.
                        Codes : 0 = critères techniques remplis, validation humaine encore obligatoire ;
                        2 = HOLD, 3 = erreur locale, 130 = annulation. Aucun démarrage de l'étape 2.
                        """);
                    return 0;
                case "--output": output = Value(); break;
                case "--timeout": timeout = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--skip-sdk": skipSdk = true; break;
                case "--login": login = true; break;
                case "--copilot-path": cliPath = Path.GetFullPath(Value()); break;
                case "--github-cycle-credits": expected = decimal.Parse(Value(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture); break;
                case "--github-cycle-month": month = Value(); break;
                default: throw new ArgumentException();
            }
        }
        if (timeout is < 1 or > 120 || expected < 0 || (expected.HasValue != (month is not null)) ||
            month is not null && !DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException();
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
    { Console.Error.WriteLine("[ERREUR] Arguments invalides. Utiliser --help. Aucun argument sensible n'est recopié."); return 3; }

    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    try
    {
        Console.WriteLine("Copilot Usage Probe\n");
        if (login && !await GitHubCredentials.LoginAsync(stop.Token))
        { Console.Error.WriteLine("[ERREUR] Connexion gh non terminée."); return 3; }
        GitHubCredentials? credentials;
        using (var authDeadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            authDeadline.CancelAfter(TimeSpan.FromSeconds(timeout));
            try { credentials = await GitHubCredentials.ResolveAsync(authDeadline.Token); }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested) { credentials = null; }
        }
        DiagnosticReport report;
        if (credentials is null) report = ProbeService.MissingCredentials();
        else
        {
            using var http = GitHubTransport.CreateClient(TimeSpan.FromSeconds(timeout));
            var service = new ProbeService(new GitHubTransport(http, credentials), new SdkQuotaProvider(cliPath));
            var run = await service.RunAsync(credentials, new(TimeSpan.FromSeconds(timeout), skipSdk, expected, month), stop.Token);
            report = run.Report;
            if (run.Login is { } account) Console.WriteLine($"[OK] Compte : {account} (console uniquement)");
        }
        foreach (var source in report.Sources)
        {
            Console.WriteLine($"[{source.Status}] {source.Source} : {source.Message}");
            if (source.Plan is { } plan) Console.WriteLine($"    Plan retourné : {plan}");
            foreach (var (name, value) in source.Numbers) Console.WriteLine($"    {name} : {value.ToString(CultureInfo.InvariantCulture)}");
            foreach (var (name, value) in source.Flags) Console.WriteLine($"    {name} : {value}");
            foreach (var (name, value) in source.Dates) Console.WriteLine($"    {name} : {value:O}");
            if (source.Credits is { } credits && source.Status == ProbeStatus.Available)
            {
                Console.WriteLine($"    Consommés : {credits.Consumed} AI Credits");
                if (credits.Allocation is { } a) Console.WriteLine($"    Allocation retournée : {a} AI Credits (budget individuel non confirmé)");
                if (credits.RemainingCalculated is { } remaining) Console.WriteLine($"    Solde calculé allocation − consommation : {remaining} AI Credits");
            }
        }
        var v = report.Validation;
        Console.WriteLine($"\nValidation : auth={v.Authenticated}, crédits mensuels={v.MonthlyAiCredits}, comparaison GitHub={v.MatchesGitHub?.ToString() ?? "À CONFIRMER"}, actualisation={v.AutomaticRefresh}, gestion explicite des erreurs={v.ExplicitErrorHandling}");
        Console.WriteLine($"Décision : {v.Decision}. Étape 2 suspendue jusqu'à validation explicite.");
        if (v.MatchesGitHub is null) Console.WriteLine("Comparer avec GitHub.com → Usage this cycle (AI Credits), puis relancer avec --github-cycle-credits et --github-cycle-month.");
        // Atomic replacement prevents a half-written report being mistaken for a complete run.
        var destination = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report, DiagnosticReport.JsonOptions), stop.Token);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Console.WriteLine($"[OK] Export : {output}");
        return v.TechnicallyEligible ? 0 : 2;
    }
    catch (OperationCanceledException) { Console.Error.WriteLine("[ANNULÉ] Aucun nouveau rapport complet. Un rapport précédent peut subsister ; vérifier sa date."); return 130; }
    catch (Exception) { Console.Error.WriteLine("[ERREUR] Opération locale impossible. Aucun nouveau rapport garanti ; vérifier sa date. Détails sensibles supprimés."); return 3; }
}
