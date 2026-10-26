# Validation

Les tests utilisent des profils, identifiants et données de consommation synthétiques. Aucun résultat de compte réel, token, rapport personnel ou capture de profil utilisateur ne doit être ajouté au dépôt.

## Contrôles automatisés

- Parsing des crédits, unités, allocations et dates du cycle courant.
- Distinction entre consommation nulle, information absente et données périmées.
- Sérialisation sans identité, token, réponse brute ou détail d'exception.
- Simulation HTTP locale : succès, erreurs 401/403, SSO, limites GitHub, redirections, réponses invalides ou excessives et délais dépassés.
- Démarrage et ping du runtime SDK officiel sans session ni inférence.
- Interface Windows : rendu des états, persistance dans un profil de test, entrée temporaire du Gestionnaire d'identifiants avec token fictif, puis suppression.

Les commandes et prérequis figurent dans [README.md](../README.md), [TRAY.md](TRAY.md) et le [contexte de simulation](../postman/simulations/probe.context.md). Les tests Windows doivent utiliser un environnement de test approprié et des données fictives.

## Validation manuelle locale

Vérifier localement l'accès GitHub.com, la cohérence avec **Usage this cycle** et l'actualisation. Les tests synthétiques ne prouvent ni les permissions d'un token réel ni sa durée de validité.

Les diagnostics, journaux, exécutables et données de consommation locaux sont exclus par `.gitignore`. Examiner le contenu de tout export avant partage. Ne jamais communiquer de token dans une issue, un commit ou une conversation.
