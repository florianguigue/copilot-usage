# Copilot Usage · Windows Tray

Application native WinForms / .NET 10 pour consulter les crédits Copilot du cycle courant.

## Utilisation

1. Lancer `CopilotUsage.Tray.exe`. Aucun installateur, droit administrateur, GitHub CLI ou runtime .NET à installer n'est nécessaire pour cet exécutable autonome Windows x64.
2. Au premier lancement, ouvrir **Paramètres**, coller le token validé à l'étape 1 puis **Enregistrer**. Le champ est masqué. L'application teste l'accès aux crédits avant de mémoriser le token dans le **Gestionnaire d'identifiants Windows**, sous `CopilotUsage/GitHub.com`.
3. Le clic gauche sur l'icône ouvre ou masque le récapitulatif. Le clic droit donne accès à **Actualiser**, **Paramètres** et **Quitter**. Relancer le programme ouvre le panneau de l'instance existante.

Si aucune entrée Windows n'est mémorisée, la connexion existante `COPILOT_USAGE_TOKEN`, puis GitHub CLI pour `github.com`, reste utilisable. Pour le démarrage Windows, mémoriser le token évite de dépendre de la variable d'un terminal. **Oublier le token** supprime uniquement l'entrée de cette application ; la variable locale et GitHub CLI restent utilisables.

## Ce qui est affiché

- Nombre exact de crédits IA consommés sur le **cycle courant** confirmé par GitHub.
- **Coût estimé en euros = crédits IA ÷ 100**, selon la formule demandée. Exemple fictif : `854,25 crédits → 8,54 €` affichés. Le calcul conserve sa précision décimale, l'affichage monétaire est arrondi au centime.
- Période du cycle en UTC, date de synchronisation et prochaine lecture.

L'icône affiche les crédits par défaut ; les paramètres permettent de choisir les euros estimés. La place dans une icône étant limitée, les grandes valeurs sont abrégées (`1,2k`) ; le panneau et l'infobulle donnent les valeurs détaillées. Une barre cyan identifie les crédits, verte les euros. Le coût est une estimation selon ta règle, sans conversion de devises ni montant de facture récupéré.

Sous Windows 11, l'icône peut être rangée dans les icônes masquées : utiliser la flèche près de l'horloge et la faire glisser dans la zone visible.

## Actualisation et erreurs

Lecture automatique toutes les **5 minutes**, configurable entre 1 et 120 minutes. Une seule lecture peut être en cours. Les limites GitHub et leur date de reprise sont respectées, y compris pour une actualisation manuelle.

Si le réseau échoue, le token expire, SSO est requis ou les données sont absentes/incompatibles, l'icône devient `!` et les deux mesures courantes affichent `—`. La dernière lecture conservée apparaît séparément, avec son cycle et sa date. Une consommation réelle de zéro affiche bien `0`. La reprise après erreur est automatique à la lecture suivante ou via **Actualiser**.

Une lecture expire au changement de cycle ou après 15 minutes sans lecture fraîche, conformément à la sonde. Un intervalle supérieur à 15 minutes peut donc afficher temporairement un état indisponible avant la lecture suivante. L'horodatage de la source, lorsqu'il est retourné, reste soumis aux mêmes contrôles de fraîcheur.

## Paramètres et fichiers locaux

Les paramètres et **une seule dernière lecture** sont stockés dans `%LOCALAPPDATA%/CopilotUsage/`. Aucun token ne figure dans ces JSON. Le changement ou la suppression du token invalide la dernière lecture de l'interface. Aucun historique de consommation ni envoi vers un serveur tiers.

Le démarrage Windows est désactivé par défaut. La case **Ouvrir à la connexion Windows** ajoute l'exécutable courant dans la clé `Run` du compte utilisateur ; la désactiver retire cette entrée. Conserver l'exécutable à un emplacement stable après activation. Quitter supprime l'icône et arrête les lectures.

L'application réutilise `GitHubTransport`, `UsageParser` et l'authentification de `Core`. Elle interroge uniquement la source de crédits validée `/copilot_internal/user`, sans relancer les diagnostics de facturation ou le runtime Copilot SDK à chaque cycle. La publication Tray n'embarque donc pas le runtime Copilot natif. L'endpoint reste non documenté ; un changement de format produit un état indisponible.

## Compilation et vérification

```powershell
dotnet build CopilotUsage.slnx -c Release
./tools/Publish-Tray.ps1
```

Sortie : `artifacts/tray-win-x64/CopilotUsage.Tray.exe`. Le fichier embarque .NET et WinForms ; son premier lancement extrait les composants dans le répertoire temporaire utilisateur.

Les tests métier utilisent le runner `CopilotUsage.Tests` et la simulation Postman existante. Le mode explicite `--self-test <dossier>` vérifie aussi le rendu natif, les icônes, la persistance et un aller-retour avec un token **fictif** dans une entrée Windows temporaire unique, supprimée ensuite. Avec `--simulation http://127.0.0.1:4917`, il exécute le contrôleur de l'application et sa vraie récupération HTTP dans la boucle Windows, avec succès, erreur 401 puis reprise. Aucun token réel ni démarrage automatique n'est modifié par ces contrôles.
