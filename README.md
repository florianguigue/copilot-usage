# Copilot Usage

Application C# / .NET 10 en trois projets : `Core` pour la récupération réutilisable, `Probe` pour le diagnostic et `Tray` pour l'application native Windows.

## Application Windows

Lancer **`artifacts/tray-win-x64/CopilotUsage.Tray.exe`**, puis ouvrir **Paramètres** pour mémoriser le token validé dans le Gestionnaire d'identifiants Windows.

Le panneau affiche les **crédits IA du cycle courant** et le **coût estimé en euros = crédits ÷ 100**. Exemple fictif : `854,25 crédits → 8,54 €`. L'icône affiche les crédits par défaut. Clic gauche : récapitulatif. Clic droit : actualiser, paramètres, quitter.

Actualisation toutes les cinq minutes, choix crédits/euros dans l'icône, fréquence configurable, démarrage Windows facultatif et dernière lecture conservée avec sa date. Les erreurs affichent un état indisponible, distinct de zéro.

Voir [le guide Windows](docs/TRAY.md). Pour compiler l'exécutable autonome : `./tools/Publish-Tray.ps1`.

## Sonde CLI : lancer sans installer .NET

L'exécutable autonome Windows x64 est dans `artifacts/win-x64/CopilotUsage.Probe.exe`. Il embarque .NET et le runtime natif officiel Copilot du SDK. Au premier lancement, .NET extrait des fichiers dans le répertoire temporaire utilisateur ; aucun service ni droit administrateur nécessaire. Garder les notices du dossier de livraison.

Le token doit être compatible avec GitHub.com **et** les sources interrogées. Un token permettant `/user` ne garantit pas l'accès à Copilot. Respecter les restrictions OAuth, SSO et Copilot CLI de l'entreprise. Ne pas emprunter l'identité OAuth d'un autre outil, extraire des cookies du navigateur ou demander des droits administrateur pour cette sonde.

Pour saisir un token sans l'inscrire dans l'historique PowerShell :

```powershell
./tools/Run-Probe.ps1
```

Ce script demande le token avec saisie masquée et définit `COPILOT_USAGE_TOKEN` uniquement dans son environnement, le temps du lancement. Il restaure ensuite l'état précédent. Si la variable existe déjà dans ton terminal :

```powershell
./artifacts/win-x64/CopilotUsage.Probe.exe --output diagnostic.json
```

La sonde n'enregistre aucun token. Sans cette variable, elle peut réutiliser `gh auth token --hostname github.com`. La connexion interactive `--login` est facultative et passe par l'application OAuth propre à GitHub CLI ; son stockage est géré par `gh`. Aucun identifiant OAuth tiers n'est recopié.

## Valider le résultat

1. Lancer la sonde et consulter `diagnostic.json`.
2. Lire **AI Credits** dans GitHub.com → Usage this cycle. Vérifier le compte et le mois UTC ; ne pas saisir un nombre de requêtes premium ou un montant en dollars.
3. Relancer en indiquant la valeur et le mois réellement observés :

```powershell
./tools/Run-Probe.ps1 -GitHubCycleCredits 854.25 -GitHubCycleMonth '2026-10'
```

Les nombres ci-dessus sont fictifs. La comparaison accepte 0,01 crédit d'écart ; une consommation pendant les deux mesures peut imposer une nouvelle comparaison. Une seconde requête HTTP, effectuée dans chaque exécution réussie, teste l'actualisation sans intervention. Cela ne prouve pas la longévité du token : relancer plus tard permet de la vérifier. Un token expiré produit un état explicite, jamais zéro.

La sonde qualifie chaque diagnostic : `HOLD` signale des données insuffisantes et `AWAITING_USER_VALIDATION` des critères techniques remplis. Codes de sortie de la sonde : `0` critères techniques remplis, `2` attente/données insuffisantes, `3` erreur locale, `130` annulation. Vérifier `generatedAtUtc` : une annulation ou erreur d'écriture peut laisser un ancien rapport sur disque.

Le rapport est déjà sans login, ID utilisateur/entreprise, URL SSO, réponse brute, token ou détail d'exception. La console seule affiche le login. Les volumes et dates restent des informations de consommation : les examiner avant partage. La sonde n'envoie rien à un serveur tiers ; ses appels sont destinés à GitHub. La télémétrie directe du runtime natif et OpenTelemetry sont désactivés ; aucune session ou requête à un modèle n'est créée.

## Sources et limites

| Source | Interprétation |
| --- | --- |
| SDK officiel `account.getQuota` | Quotas d'interactions et réinitialisation, sans conversion en crédits |
| `/copilot_internal/user` | Source non documentée : crédits seulement si `token_based_billing=true` et `credits_used` numérique |
| Facturation personnelle `ai_credit/usage` | Diagnostic isolé ; ne prouve pas l'usage financé par l'entreprise |

Une allocation retournée n'est pas déclarée « budget individuel ». Le solde est une **valeur calculée** `allocation − consommation`, uniquement lorsque `unlimited=false` et que l'allocation est présente. Il peut être négatif. Aucune conversion dollars/crédits, aucun budget supposé. L'absence d'allocation ne bloque pas la lecture des crédits consommés.

Pour confirmer un cycle courant, le fournisseur interne exige une réinitialisation au premier jour du mois UTC suivant. Sans cette preuve, les chiffres restent diagnostiques et ne valident pas le go/no-go. Un horodatage source fourni datant de plus de 15 minutes est considéré périmé ; son absence n'est pas remplacée par un faux horodatage source. `fetchedAtUtc` date seulement la lecture. Les modifications de champs, mauvais types, données négatives ou JSON incorrect sont signalés explicitement.

Les détails contractuels, références exactes et limites connues figurent dans [docs/SOURCES.md](docs/SOURCES.md). Les contrôles de validation figurent dans [docs/VALIDATION.md](docs/VALIDATION.md).

## Compiler et tester

Avec un SDK .NET 10 :

```powershell
dotnet restore CopilotUsage.slnx
./tools/Test.ps1
./tools/Publish.ps1
```

Dans cet espace de travail, un SDK local a été préparé dans `../.tools/dotnet`. On peut le passer avec `-Dotnet (Resolve-Path ../.tools/dotnet/dotnet.exe)`.

Tests HTTP locaux avec Postman CLI 1.73 ou ultérieur, deux terminaux depuis ce dossier :

```powershell
postman simulation run postman/simulations/probe.sim.yaml --port 4917 --no-history --output ndjson
```

```powershell
./tools/Test.ps1 -WithSimulation -SdkSmoke
```

Les fixtures utilisent un token fictif exclusivement local. `-SdkSmoke` vérifie le démarrage et un ping du runtime natif officiel, sans requête à un modèle. Les tests du SDK restent distincts des simulations HTTP ; un faux fournisseur SDK dans les tests d'orchestration n'établit pas un accès réel à Copilot. Voir [le contexte de simulation](postman/simulations/probe.context.md).
