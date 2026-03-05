# Uptime Monitor — Plugin v1

## Objectif

Ce plugin permet de surveiller la disponibilité de services web en effectuant des vérifications HTTP périodiques sur une liste d'URLs configurées par l'administrateur.

## Fonctionnement

L'administrateur crée des **checks**, chacun associé à :

- une URL à surveiller (`http://` ou `https://`)
- un intervalle de vérification (en minutes)
- une liste d'adresses e-mail à notifier
- un statut actif/inactif

Un **worker en arrière-plan** exécute les checks selon leur intervalle configuré. Une réponse HTTP `200–399` est considérée comme **up** ; tout autre code ou échec réseau (timeout, DNS, TLS…) est considéré comme **down**.

## Alertes e-mail

Le plugin envoie des notifications uniquement lors des **transitions d'état** :

- **down** → un e-mail d'alerte est envoyé (une seule fois)
- **up** (récupération) → un e-mail de rétablissement est envoyé

Tant que le service reste dans le même état, aucun e-mail supplémentaire n'est envoyé.

## Données persistées

Pour chaque check, le plugin conserve :

- la date et l'heure du dernier test
- le résultat (up / down)
- le code HTTP retourné (si disponible)
- le message d'erreur (si la requête a échoué)

Ces informations sont stockées de façon persistante et restent disponibles après un redémarrage.

## Périmètre v1

Cette première version couvre exclusivement les fonctionnalités décrites ci-dessus. Les fonctionnalités avancées (historique, dashboards, webhooks, etc.) sont hors scope pour l'instant.
