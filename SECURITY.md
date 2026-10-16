# Sécurité et distribution — ExileCenter

## Sources et téléchargements

Les sources sont publiées dans [TahiKari/ExileCenter](https://github.com/TahiKari/ExileCenter) et les paquets dans ses [Releases](https://github.com/TahiKari/ExileCenter/releases). La documentation d'installation est dans le [README](README.md). Les anciens noms de fichiers sont conservés pour les installations issues de GameHelper P-V1.

## Mises à jour effectivement distribuées

Les exécutables du ZIP démarrent directement le cœur ExileCenter. Ce circuit n'utilise pas l'ancien lanceur à manifeste RSA. Le code de ce lanceur reste présent pour l'historique ; il ne faut pas confondre sa signature de manifeste avec une signature des paquets ExileCenter.

1. L'application consulte la dernière Release publique de **TahiKari/ExileCenter** ; les brouillons et préversions sont exclus.
2. Le nom, l'URL et la taille du ZIP sont contrôlés. Le téléchargement doit correspondre au SHA256 fourni par GitHub pour l'asset.
3. Un dossier local de mise à jour compatible peut être prioritaire ; son manifeste fournit également un SHA256 à vérifier.
4. Les chemins du ZIP sont validés avant l'extraction. Les réglages personnels sont exclus des remplacements.
5. Le programme prépare un installateur temporaire, attend sa confirmation de démarrage, puis se ferme. L'installateur remplace les fichiers et redémarre l'application. En cas d'échec de remplacement, il tente de restaurer les fichiers précédents.
6. Le binaire validé `GameOffsets.dll` est vérifié et n'est pas remplacé par ce mécanisme.

Le SHA256 contrôle l'intégrité du paquet ; il ne constitue ni un audit du code ni une signature d'auteur indépendante de GitHub. Les binaires ne disposent pas de signature Authenticode. Le compte mainteneur, le dépôt et la machine locale restent des éléments de confiance.

Vous pouvez refuser une mise à jour au démarrage et télécharger manuellement le ZIP. Pour vérifier une archive :

```powershell
Get-FileHash .\ExileCenter-win-x64.zip -Algorithm SHA256
```

Comparer le résultat au fichier `ExileCenter-win-x64.zip.sha256` de la même Release.

## Accès au jeu et données locales

L'application et ses plugins accèdent aux informations du processus du jeu. Certaines fonctions peuvent simuler des entrées, ajuster la caméra ou modifier des fichiers du jeu ; consulter les réglages du plugin concerné avant de l'activer. CorpseCleaner applique un patch réversible uniquement jeu fermé et conserve une sauvegarde locale.

Les réglages et historiques restent dans le dossier de l'application et ceux des plugins. Certaines fonctions de prix et le mécanisme de mise à jour utilisent Internet. Ne pas publier des configurations, journaux ou captures contenant des informations personnelles.

## Alerte Windows ou antivirus

En cas de blocage, relever le message, contrôler l'adresse de téléchargement, la version et le SHA256. Une alerte ne doit pas être considérée automatiquement comme un faux positif. Ne pas désactiver globalement l'antivirus ; en cas de doute, suspendre l'installation et signaler le problème.

## Signaler un problème

Utiliser les [tickets ExileCenter](https://github.com/TahiKari/ExileCenter/issues) pour un problème reproductible sans information sensible. Indiquer la version affichée, la version du jeu et le plugin concerné. Pour une vulnérabilité sensible, utiliser le signalement privé GitHub lorsqu'il est disponible et éviter de publier des secrets ou des détails permettant une exploitation immédiate.
