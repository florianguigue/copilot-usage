# Simulation locale Copilot Usage Probe

Fixtures synthétiques dérivées du schéma GHCPSpendTray figé dans `docs/SOURCES.md` et des exemples de facturation GitHub. Aucune fixture n'est issue du compte réel. Recherche de mock propriétaire Postman omise : aucune connexion Postman demandée pour cette sonde locale.

Commande : `postman simulation run postman/simulations/probe.sim.yaml --port 4917 --no-history --output ndjson`. Tests : `tools/Test.ps1 -WithSimulation`. La sonde Core effectue de vrais appels HTTP, avec un token fictif. Les chemins supplémentaires de scénario sont réservés à ce serveur local, jamais envoyés à GitHub.

Couverture : succès avec allocation, sans allocation, usage absent, ancien quota, cycle expiré, comparaison incohérente, échec de seconde lecture et diminution de consommation ; 401, 403, SSO, rate limit, 404, 503, HTML, JSON tronqué, redirection, taille excessive et délai dépassé.

Le SDK n'est pas membre de la simulation : un faux fournisseur sert seulement à isoler l'orchestration HTTP. Ni les quotas SDK réels ni les permissions GitHub du compte ne sont validés par cette simulation. Le serveur refuse les méthodes autres que GET et vérifie Bearer, User-Agent et version REST. Toute route non modélisée renvoie 501.

Particularité constatée avec Postman CLI 1.73.0 : `path` doit viser `config.yaml`, malgré la documentation de compétence qui accepte aussi un dossier. Les journaux ne doivent contenir que les fixtures et le token fictif.

Exécution du 26/10/2026 avec `-WithSimulation -SdkSmoke` : **48 tests réussis, 0 échec**, dont 26 contrôles de parsing/confidentialité, 21 contrôles HTTP/orchestration et un démarrage/ping du runtime officiel sans inférence. Le membre `github` a effectivement servi 41 requêtes par suite, visibles dans le journal (trois lectures dans les cas sans crédits valides, quatre sinon). Aucun membre silencieux ; aucun membre SDK. Le démarrage dans le bac à sable a demandé l'accès au stockage local Postman ; exécution locale autorisée ensuite, sans historique distant.

Étape 2, même date : **55 contrôles métier/HTTP/SDK** réussis, plus **7 contrôles Windows**. La nouvelle lecture périodique utilise `CurrentCycleUsageService`. Le contrôleur `TrayContext` a tourné dans la vraie boucle de messages Windows avec un profil de fichiers isolé et le token fictif ; trois appels sur `/tray-refresh-<guid>/copilot_internal/user` ont produit 200, 401, 200. La valeur courante disparaît au 401, la dernière lecture reste séparée et la reprise rétablit les crédits et leur estimation en euros. Le membre `github` a servi à la fois les tests Core et ce contrôleur ; aucun membre silencieux. Les tests Windows Credential Manager doivent être lancés dans une session Windows utilisateur, le bac à sable ne disposant pas d'un jeu d'identifiants (erreur 1312).
