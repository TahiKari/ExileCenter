# Instructions de travail — GameHelper P-V1

Consulter ce fichier avant chaque intervention dans ce dépôt. Répondre en français.

- Lire uniquement les fichiers nécessaires, appliquer un patch minimal et préserver les modifications existantes.
- Aucun audit global, refactor massif ou installation de dépendances sans demande explicite.
- Ne jamais reconstruire ni remplacer `GameOffsets.dll` ; conserver le binaire validé à l'identique.
- Ne pas arrêter GameHelper, déplacer la souris ni cliquer dans le jeu pendant que l'utilisateur joue. Conserver l'arrêt de sécurité lorsque la souris bouge.
- Exécuter les fixtures .NET avec `scripts/run-check.ps1`, dans des dossiers de test isolés.

## Publication après chaque mise à jour de l'application

L'utilisateur autorise la publication locale et sur GitHub après chaque mise à jour validée.

1. Compiler les changements et effectuer les contrôles adaptés, sans reconstruire GameOffsets.
2. Envoyer les sources sur `TahiKari/Gamehelper2`, créer un nouveau tag et laisser le workflow GitHub compiler le paquet complet sur le runner Windows standard du dépôt public.
3. Vérifier le résultat de la CI, le ZIP et son SHA256, puis publier la nouvelle Release publique avec les changements expliqués. Un simple push ou une Release laissée en brouillon ne suffit pas.
4. Préparer et publier aussi la mise à jour locale dans `C:\Users\tahka\Desktop\Gamehelpers\Maj GameHelper`, sans modifier l'installation en cours ni ses réglages.
5. Faire évoluer la version dans le nom affiché à chaque Release : **GameHelper P-V1.0**, **GameHelper P-V1.1**, etc. Augmenter le dernier numéro pour une correction et choisir la progression adaptée aux changements. Conserver les liens de téléchargement visibles et les identifiants techniques compatibles avec les versions déjà installées. La version intégrée au binaire doit correspondre au tag publié.

Pour une modification limitée à la documentation ou aux libellés GitHub, ne pas reconstruire l'application ni créer une nouvelle Release binaire inutile.

Réponse finale : fichiers modifiés, résultat, tests effectués, prochaine action et lien de la Release lorsqu'une mise à jour est publiée.
