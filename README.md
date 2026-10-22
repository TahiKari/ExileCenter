# ExileCenter

**Ton compagnon sur Wraeclast.**

ExileCenter rassemble dans une interface modulaire les outils de suivi et d'assistance pour **Path of Exile 2** : butin, dégâts, cartes, alertes et gestion de l'inventaire. Le projet est maintenu par [TahiKari](https://github.com/TahiKari), avec son propre dépôt, ses Releases et son circuit de mises à jour.

## Télécharger

### [Télécharger ExileCenter — Windows x64 (ZIP)](https://github.com/TahiKari/ExileCenter/releases/latest/download/ExileCenter-Complete-win-x64.zip)

[Téléchargeur ExileCenter](https://github.com/TahiKari/ExileCenter/releases/latest/download/ExileCenterDownloader.exe)

[Dernière version et changements](https://github.com/TahiKari/ExileCenter/releases/latest) · [Toutes les versions](https://github.com/TahiKari/ExileCenter/releases) · [Signaler un problème](https://github.com/TahiKari/ExileCenter/issues)

## Prérequis

| Élément | Pour utiliser ExileCenter |
| --- | --- |
| Système | Windows 10 ou 11 **64 bits**, sur processeur x64 |
| Jeu | Une installation de Path of Exile 2 ; les outils en jeu nécessitent que le jeu soit lancé |
| Runtime | **[.NET 10 Desktop Runtime pour Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)** : choisir « .NET Desktop Runtime », puis « Windows x64 » |
| Affichage | Pilote graphique à jour et prise en charge Direct3D 11 pour la superposition |
| Internet | Téléchargement, recherche de mises à jour et fonctions utilisant des prix en ligne |

Le Desktop Runtime inclut le runtime .NET de base : il suffit pour utiliser le ZIP. Le SDK .NET, Visual Studio, Git, Node.js et Python ne sont pas nécessaires pour jouer. Choisir la version **x64**, même sur un Windows où un runtime x86 est déjà installé. Voir les [instructions Microsoft](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

## Installation, étape par étape

1. Installer **.NET 10 Desktop Runtime x64** avec le lien ci-dessus.
2. Télécharger **ExileCenter-win-x64.zip** depuis la dernière Release de ce dépôt.
3. Dans les propriétés du ZIP, utiliser « Débloquer » si Windows propose cette option, puis extraire **tout** le contenu dans un dossier dédié, par exemple `C:\Jeux\ExileCenter`.
4. Lancer **ExileCenter.exe** depuis le dossier extrait et accepter la demande Windows de droits administrateur. Le manifeste actuel de l'application demande ces droits.
5. Démarrer Path of Exile 2. Le mode fenêtré sans bordure facilite l'affichage de la superposition.
6. Ouvrir les paramètres avec **F11** par défaut, choisir la langue et activer les plugins souhaités. La touche reste personnalisable.

Ne lancer aucun exécutable directement dans le ZIP et conserver les DLL, les ressources et le dossier `Plugins` à côté de l'application.

## Ce que propose le programme

Chaque plugin dispose de ses réglages. Les outils utilisant des raccourcis ou des actions automatiques demandent une configuration adaptée à votre jeu ; leur présence dans le paquet ne signifie pas qu'ils sont tous activés.

| Outil | Fonction |
| --- | --- |
| **Suivi du butin — LootTracker** | Sessions de farm, gains estimés en devises, profit par heure, historique et temps passé par carte ; activation et pause du suivi |
| **Kikimeter — KikiMeter** | Estimation des dégâts et du DPS à partir des données observables, pics, comparaison et historique des cartes ; ce n'est pas un relevé serveur exhaustif |
| **SekhemaHelper** | Aide à la lecture du parcours des épreuves |
| **Timers de Sekhema** | Tentative séparée à chaque nouveau Barya détecté, pause hors tentative, arrêt après Zarokh observé et quatre sous-timers dans LootTracker et KikiMeter |
| **Atlas / Atlas2** | Outils et informations pour l'Atlas et les cartes |
| **Radar / WorldDrawing** | Repères et informations visuelles sur l'environnement |
| **PreloadAlert** | Alertes fondées sur les ressources chargées par le jeu |
| **HealthBars / SimpleBars** | Barres de vie et d'énergie personnalisables |
| **AuraTracker / PlayerBuffBar / Hiveblood** | Suivi d'auras, d'effets et de ressources du personnage |
| **AmanamuVoidAlert / RitualWispAlert** | Alertes liées à certaines mécaniques et dangers |
| **RitualHelper** | Informations et assistance pour les récompenses de Rituel |
| **RunecraftHelper** | Outils liés aux runes et à l'artisanat |
| **LootValue / StashValueByZx0** | Estimation de la valeur des objets ou du contenu du coffre selon les données de prix disponibles |
| **PickupHelper / StashUtility** | Assistance au ramassage et au rangement, avec réglages et protections d'objets |
| **AutoPot / AutoHotKeyTrigger** | Déclenchements configurables de flacons ou de raccourcis |
| **CorpseCleaner** | Masquage réversible des cadavres par modification des fichiers du jeu, à effectuer jeu fermé |

L'interface comprend des thèmes sombres distincts par plugin, plusieurs langues et des fenêtres indépendantes pour le suivi du butin et le kikimeter. Les fonctions dépendent des données accessibles et de la version du jeu ; un changement de PoE 2 peut nécessiter une adaptation.

### Dépendance optionnelle : CorpseCleaner

Ce plugin demande le dossier de votre installation du jeu et une bibliothèque **Oodle compatible (`oo2core.dll`) fournie par votre propre installation ou outil local**. Elle n'est pas distribuée dans le ZIP. Le plugin peut aussi lire cette bibliothèque depuis un exécutable Exile Forge déjà présent, sans lancer cet exécutable.

Le jeu doit être fermé pour appliquer ou restaurer le patch. Pour le client autonome, prévoir assez d'espace pour une copie de `Content.ggpk`. Les nouvelles sauvegardes utilisent `.ExileCenterCadavres`. Les anciennes sauvegardes restent reconnues pour permettre leur restauration sans altération. Cette dépendance n'est pas requise pour les autres plugins.

## Mettre à jour et conserver ses réglages

Au démarrage, ExileCenter peut proposer une mise à jour avec le choix de l'installer ou de continuer. Pendant l'utilisation, une notification signale les nouvelles versions. Dans la page **Mises à jour**, le bouton d'installation télécharge le paquet, vérifie son SHA256 et redémarre l'application après confirmation. Une connexion indisponible n'empêche pas le démarrage.

Les dossiers `configs/`, les configurations de plugins et les fichiers `settings.json` ne sont pas fournis dans les paquets de mise à jour. Sauvegarder votre dossier avant une installation manuelle ; ne pas mélanger les fichiers de plusieurs versions.

### Depuis GameHelper P-V1

Les exécutables, DLL, fichiers .NET, espaces de noms du cœur et références des 25 plugins utilisent désormais **ExileCenter**. Le paquet complet se lance avec **ExileCenter.exe**.

Les anciens installateurs reçoivent un paquet de transition via leur URL habituelle. Son petit programme de migration met en place les fichiers ExileCenter, relance l’application puis retire uniquement les anciens exécutables et DLL connus. Les réglages, les raccourcis personnels vers cette installation et `GameOffsets.dll` sont préservés ; les raccourcis du Bureau et du menu Démarrer sont redirigés vers ExileCenter. Les anciens noms restent seulement dans les chemins de compatibilité et les archives historiques. Le code de transition est distinct du paquet complet destiné aux nouvelles installations.
Pour l'installation personnelle de TahiKari, le dossier local `Bureau\ExileCenter\Maj ExileCenter` reste prioritaire. Les archives et le manifeste portent le préfixe `ExileCenter-Maj`. Chaque publication conserve uniquement le nouveau paquet et son prédécesseur, après vérification du paquet et publication du manifeste ; les autres fichiers du dossier ne sont pas supprimés. Les anciens chemins et manifestes restent lisibles pour la migration.

### Tablettes : couleurs et presets

Les rangs utilisent une palette commune à la grille, aux cadres et aux badges : **S doré, A rouge, B violet, C bleu, D vert, E orange cuivré, F blanc**. La couleur suit le meilleur modificateur sélectionné qui satisfait son minimum ; un seul suffit, sans exiger quatre choix. Les sous-rangs numérotés restent affichés et permettent de trier les combinaisons. Un modificateur décoché n'est pas réactivé par le catalogue de référence.

Dans **Coffre et inventaire → Filtres → Tablettes**, cinq presets permettent de sauvegarder puis de charger les rangs, minimums et règles ET / OU des huit types de tablettes. Choisir un emplacement, utiliser l'icône disquette pour le sauvegarder et l'icône de chargement pour le rappeler. Charger un emplacement vide est désactivé. Les presets font partie des réglages personnels et sont conservés lors des mises à jour.

## Résoudre les problèmes courants

- **« You must install or update .NET »** : installer le **Desktop Runtime .NET 10 x64**, puis relancer. Le runtime ASP.NET seul et l'ancien .NET Framework ne suffisent pas.
- **Fenêtre absente ou superposition invisible** : vérifier que le jeu est ouvert, essayer le mode fenêtré sans bordure et vérifier la touche d'ouverture des paramètres.
- **Plugin manquant ou erreur de DLL** : extraire le ZIP complet dans un nouveau dossier, sans réutiliser les DLL d'une autre version.
- **Valeurs absentes après une mise à jour du jeu** : consulter les notes de la dernière Release et ouvrir un ticket avec les versions du jeu et d'ExileCenter.
- **Blocage Windows ou antivirus** : consulter l'alerte et vérifier la provenance du téléchargement ainsi que le SHA256. Les binaires ne sont pas signés Authenticode ; ne pas désactiver globalement les protections. Voir [SECURITY.md](SECURITY.md).

Pour un ticket, joindre la version affichée, la version du jeu, le plugin concerné et les étapes permettant de reproduire le problème. Retirer les informations personnelles des journaux et captures.

## Sources et compilation

Pour afficher le projet sous le nom **ExileCenter** dans l'explorateur de VS Code ou Cursor, ouvrir [`ExileCenter.code-workspace`](ExileCenter.code-workspace). Le dossier du cœur et le projet sont dans `ExileCenter/`, et la solution se nomme `ExileCenter.sln`. Cet espace de travail conserve les chemins relatifs et fonctionne même si Windows verrouille encore le nom physique d'un ancien dossier `GameHelper2` ouvert.

Pour développer : Windows x64, **SDK .NET 10**, Git et éventuellement un IDE C#. Le SDK inclut les runtimes. Le dépôt contient le code du cœur, des plugins et la chaîne de publication.

```powershell
git clone https://github.com/TahiKari/ExileCenter.git
cd ExileCenter
$targets = Join-Path $PWD '.github/ci/PreserveOffsets.targets'
$offsets = Join-Path $PWD '.github/ci/GameOffsets.dll'
$properties = @("-p:DirectoryBuildTargetsPath=$targets", "-p:PinnedGameOffsetsPath=$offsets")
$plugins = [regex]::Matches((Get-Content ExileCenter.sln -Raw), '"(Plugins\\[^"\r\n]+\.csproj)"') |
    ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
foreach ($project in @('ExileCenter/ExileCenter.csproj') + @($plugins)) {
    dotnet restore $project @properties
    if ($LASTEXITCODE -ne 0) { throw "Restauration échouée : $project" }
    dotnet build $project -c Release --no-restore -p:BuildProjectReferences=false @properties
    if ($LASTEXITCODE -ne 0) { throw "Compilation échouée : $project" }
}
$runtime = Join-Path $PWD 'ExileCenter/bin/Release/net10.0-windows/win-x64'
foreach ($path in @(git ls-files -- 'Plugins/*/Localization/*.json')) {
    $destination = Join-Path $runtime $path
    [void][IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
    Copy-Item -LiteralPath $path -Destination $destination
}
$native = Join-Path $runtime 'runtimes/win-x64/native'
if (Test-Path $native) {
    Get-ChildItem $native -Filter '*.dll' -File | Copy-Item -Destination $runtime
}
```

Les sorties sont dans `ExileCenter\bin\Release\net10.0-windows\win-x64`. Pour préparer un ZIP de test isolé :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1 -LocalUpdate -SkipBuild -LocalDestination "$PWD\LocalUpdateRelease\test" -LocalLabel "ExileCenter — test local"
```

Extraire ce ZIP dans un dossier de test puis lancer `ExileCenter.exe`. La copie validée de `GameOffsets.dll` est utilisée telle quelle ; sa reconstruction est bloquée par `PreserveOffsets.targets`. Ne pas utiliser une reconstruction globale de la solution qui contournerait cette protection.

Le workflow [release.yml](.github/workflows/release.yml) compile sur un runner Windows du dépôt public ExileCenter. Un tag prépare une Release en brouillon ; le paquet complet, son SHA256 et les contrôles doivent être vérifiés avant sa publication. Les fixtures .NET s'exécutent avec `scripts/run-check.ps1` dans un dossier isolé.

## Origines, crédits et licence

ExileCenter est maintenu indépendamment sur GitHub, mais réutilise du code de la lignée **GameHelper / GameHelper2**, notamment la base maintenue par MordWraith et les contributions de Lafko, Gordin et de la communauté. Le changement de nom ne change pas cette origine. L'historique, les crédits des auteurs et les licences sont conservés dans [CREDITS.md](CREDITS.md) et les sources.

Le projet est distribué sous **[GNU GPL v3](LICENSE)**. CorpseCleaner et ses bibliothèques LibGGPK utilisent les conditions précisées dans leur [licence AGPLv3](Plugins/CorpseCleaner/LICENSE). Les autres dépendances conservent leurs licences respectives.

ExileCenter est un projet communautaire, sans affiliation avec Grinding Gear Games. Path of Exile et les éléments du jeu appartiennent à leurs ayants droit.
