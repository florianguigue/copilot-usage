# Contrats et références vérifiés le 26 octobre 2026

## Application Windows et estimation en euros

Le Tray réutilise le transport et le parseur de la sonde. Sa lecture périodique n'appelle que `/copilot_internal/user`. Le runtime SDK et la facturation personnelle restent des outils du diagnostic CLI.

La formule `crédits IA ÷ 100 = euros estimés` est une convention explicitement demandée par l'utilisateur, appliquée en `decimal`, avec affichage arrondi au centime. Il ne s'agit pas d'un taux de change ni d'un montant de facture reçu de GitHub.

Le token est mémorisé via les API Windows [CredWriteW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew) / [CredReadW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credreadw), dans une entrée générique propre à l'application, pour l'utilisateur courant et ce PC. La notification utilise [WinForms NotifyIcon](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0), avec une image numérique générée et des handles natifs libérés après copie.

## SDK officiel

Package NuGet `GitHub.Copilot.SDK` **1.0.26**, version exacte dans le projet et les fichiers de verrouillage. Le package fixe le runtime Copilot **1.0.103** et vérifie le SHA256 de son téléchargement à la compilation. API utilisée : `client.Rpc.Account.GetQuotaAsync(gitHubToken: ..., cancellationToken: ...)`. L'API générée est expérimentale (`GHCP001`) ; suppression locale à l'adaptateur uniquement.

- [Package officiel](https://www.nuget.org/packages/GitHub.Copilot.SDK/1.0.26)
- [Documentation GitHub des quotas et unités](https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/usage-and-billing)
- [Code SDK consulté](https://github.com/github/copilot-sdk/tree/c57638f4a7ce2255a34192d5035932d0e109bbdd/dotnet)

Le résultat typé fournit `quotaSnapshots` ; seules les clés connues `premium_interactions`, `chat`, `completions` sont exportées. Champs SDK : `entitlementRequests`, `usedRequests`, `remainingPercentage`, `overage`, indicateurs d'illimité/dépassement et `resetDate`. Ces champs restent dans leur unité SDK. Les champs numériques requis utilisent les types non nullables du SDK ; la sonde ne dispose pas de la présence JSON brute de ces champs. Aucun de ces nombres ne contribue au critère AI Credits mensuels.

Le SDK lance son runtime natif via stdio, avec le token dans l'environnement du processus et `UseLoggedInUser=false`, pour éviter une sélection implicite d'un autre compte. Il ne crée aucune session. Les erreurs détaillées du SDK sont supprimées car elles peuvent contenir des informations sensibles. Les codes HTTP structurés 401/403 sont disponibles pour les appels HTTP directs ; un échec SDK non structuré reste `Unavailable`, sans prétendre connaître son statut HTTP.

## Source interne (instable)

Référence consultée : GHCPSpendTray, commit [`714d16daf191db9699ddb0fa61aaef876f4ad6ac`](https://github.com/DamianEdwards/ghcp-spend-tray/blob/714d16daf191db9699ddb0fa61aaef876f4ad6ac/src/GHCPSpendTray.Core/HttpServices.cs). Implémentation indépendante, sans copie de son UI ni de son identité OAuth.

`GET https://api.github.com/copilot_internal/user`, Bearer token, réponse attendue :

```json
{
  "copilot_plan": "enterprise",
  "quota_reset_date_utc": "2026-11-01T00:00:00Z",
  "quota_snapshots": {
    "premium_interactions": {
      "token_based_billing": true,
      "credits_used": 854.25,
      "entitlement": 1500,
      "unlimited": false,
      "has_quota": true,
      "timestamp_utc": "2026-10-26T10:00:00Z"
    }
  }
}
```

Exemple synthétique. Les champs sont facultatifs pour le diagnostic mais les crédits et la preuve de cycle sont indispensables à l'éligibilité. `quota_reset_date` est accepté en repli. Les dates ISO 8601 et les nombres entiers de secondes Unix sont acceptés, conformément au fournisseur de référence. `has_quota=false` peut signaler un épuisement et n'invalide pas une consommation retournée. Aucun appel d'administration, jeton de navigateur, échange OAuth d'une application tierce, contournement de SSO ou élargissement automatique de scopes.

## Facturation personnelle

`GET /users/{login}/settings/billing/ai_credit/usage?year=YYYY&month=M`, version REST `2026-03-10`. La réponse `usageItems` peut être vide. On isole les lignes Copilot de type `credits`/`ai-credits`, leurs quantités brutes et nettes et `timePeriod`. Les dollars et les requêtes sont exclus de ces totaux.

[GitHub Billing usage](https://docs.github.com/en/rest/billing/usage) précise que les endpoints utilisateur ne contiennent pas l'usage payé par une organisation ou une entreprise. Ce fournisseur ne peut donc jamais, à lui seul, valider ce projet. 401/403/404 et réponses vides sont des diagnostics utiles.

## Transport et confidentialité

Appels GET uniquement, API GitHub.com fixe en production, TLS normal, redirections refusées, cookies désactivés, délai et taille bornés, cache HTTP désactivé. Les URI loopback sont acceptées uniquement par le constructeur `Core` pour les tests ; aucune option CLI ne permet de rediriger un vrai token vers un hôte tiers.

Le diagnostic exporte une liste blanche de nombres, booléens, dates et noms de plans connus, jamais les réponses brutes. Aucun journal externe. `COPILOT_GITHUB_TELEMETRY_DISABLED=true` désactive les envois directs du runtime natif ; `OTEL_SDK_DISABLED=true` désactive l'export OpenTelemetry. La vérification réelle du compte et des données reste nécessaire.

Découverte Orbit effectuée avec une recherche générique sans données privées : les résultats proposés concernaient les métriques d'organisation et ne répondaient pas à la lecture personnelle sans droits administrateur. Ils n'ont pas été retenus ; les contrats ci-dessus proviennent des sources primaires GitHub.
