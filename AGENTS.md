# Instructions de travail — ExileCenter

Consulter ce fichier avant chaque intervention dans ce dépôt. Répondre en français.

- Lire uniquement les fichiers nécessaires, appliquer un patch minimal et préserver les modifications existantes.
- Modifier uniquement le comportement demandé. Ne pas déplacer, réorganiser, masquer ou redimensionner des icônes, boutons ou barres sans demande explicite.
- Préserver les réglages personnalisés, notamment la position de la barre du coffre et de ses icônes, ses décalages, son ancrage et son verrouillage. Une position remise en place par l'utilisateur fait référence ; ne pas la réinitialiser lors d'une correction, d'un changement d'onglet ou d'une mise à jour.
- Prévenir les régressions dans le périmètre modifié : vérifier le comportement demandé et la conservation des réglages et de l'affichage existants avant publication. Une modification de l'auto-craft n'autorise pas à changer la barre d'icônes. Si une vérification réelle manque, le signaler sans présenter le comportement en jeu comme confirmé.
- Aucun audit global, refactor massif ou installation de dépendances sans demande explicite.
- Ne jamais reconstruire ni remplacer `GameOffsets.dll` ; conserver le binaire validé à l'identique.
- Ne pas arrêter ExileCenter (ni une ancienne instance GameHelper), déplacer la souris ni cliquer dans le jeu pendant que l'utilisateur joue. Conserver l'arrêt de sécurité lorsque la souris bouge.
- Exécuter les fixtures .NET avec `scripts/run-check.ps1`, dans des dossiers de test isolés.
- Prévention des blocages de shaders : avant toute correction ou publication liée aux réglages visuels permanents, vérifier le journal du dernier lancement de PoE et l'état exact du patch appliqué. Ne jamais conclure à une réparation durable sur un seul lancement réussi ; vérifier aussi un second lancement avec les caches reconstruits. Une compilation de test ne valide pas le rendu en jeu.
- Ne pas effacer automatiquement les caches de shaders/pipelines ni changer le moteur graphique, le pilote ou les réglages personnels lors d'une mise à jour. Toute intervention de diagnostic sur les caches doit être ciblée, réversible, jeu fermé et conserver une sauvegarde. Distinguer une cause démontrée d'une hypothèse ; signaler toute validation en jeu manquante et ne jamais garantir l'absence de futurs défauts du jeu/pilote.
- Afficher les notes de mise à jour, chemins et erreurs comme du texte littéral dans ImGui (`TextUnformatted` avec retour à la ligne si nécessaire) ; vérifier les pourcentages et accents dans la fenêtre de démarrage et dans les réglages.

## Publication après chaque mise à jour de l'application

L'utilisateur autorise la publication locale et sur GitHub après chaque mise à jour validée.

1. Compiler les changements et effectuer les contrôles adaptés, sans reconstruire GameOffsets.
2. Envoyer les sources sur `TahiKari/ExileCenter`, créer un nouveau tag et laisser le workflow GitHub compiler le paquet complet sur le runner Windows standard du dépôt public. `TahiKari/Gamehelper2` reste un dépôt historique de compatibilité ; ne pas y développer de nouvelles versions.
3. Vérifier le résultat de la CI, le ZIP et son SHA256, puis publier la nouvelle Release publique avec les changements expliqués. Un simple push ou une Release laissée en brouillon ne suffit pas.
4. Préparer et publier aussi la mise à jour locale dans `C:\Users\tahka\Desktop\ExileCenter\Maj ExileCenter`, sans modifier l'installation en cours ni ses réglages. Après validation et publication du manifeste, ne conserver que le nouveau paquet et le précédent ; supprimer uniquement les anciennes archives générées par le script de publication.
5. Depuis **ExileCenter 1.6.0**, utiliser le format **majeure.mineure.correctif**, sans préfixe P-V dans l'application : 1.6.1 pour une correction, 1.7.0 pour une évolution fonctionnelle, 2.0.0 pour une évolution majeure incompatible. Faire correspondre la version affichée, celle du binaire et le tag (`v1.6.0`, etc.). Conserver la lecture des anciens tags personnalisés et les liens de téléchargement compatibles avec les versions déjà installées.
6. Renseigner le numéro et le détail des changements dans chaque mise à jour locale (`scripts/publish.ps1 -Version <tag> -Changelog <changements>`) et dans les notes de Release GitHub, pour les afficher dans la fenêtre de mise à jour.

Pour une modification limitée à la documentation ou aux libellés GitHub, ne pas reconstruire l'application ni créer une nouvelle Release binaire inutile.

Réponse finale : fichiers modifiés, résultat, tests effectués, prochaine action et lien de la Release lorsqu'une mise à jour est publiée.
