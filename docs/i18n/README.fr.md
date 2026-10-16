<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../hero.jpg">
  <img src="../hero.jpg" alt="Clothesline. Des captures mises à sécher. Quatre captures dans des cadres en verre pendent à un fil fin en haut de l'écran.">
</picture>

<h3 align="center">Un portage Windows de <a href="https://tendedero.app/?lang=fr">Tendedero</a>&nbsp;&rsaquo;</h3>

<p align="center">
  Gratuit et open source. Pour Windows 10 et 11.
  <br>
  <a href="https://github.com/Zhao-allen/Clothesline/releases/latest">Télécharger&nbsp;&rsaquo;</a>
  &nbsp;&nbsp;
  <a href="#compiler-depuis-les-sources">Compiler depuis les sources&nbsp;&rsaquo;</a>
  <br><br>
  <a href="../README.md">English</a>&nbsp;·&nbsp;<a href="README.zh-Hans.md">简体中文</a>&nbsp;·&nbsp;<a href="README.es.md">Español</a>&nbsp;·&nbsp;<a href="README.ja.md">日本語</a>&nbsp;·&nbsp;Français
</p>

<br>

## Hors de vue. À portée de main.

Chaque capture que vous faites pend à un fil juste au-dessus de votre écran.
Appuyez sur <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> et il descend. Éloignez le pointeur et il disparaît.

<p align="center">
  <img src="../demo.jpg" alt="Le pointeur repose contre le bord supérieur, le fil descend avec les captures accrochées, et il se range quand le pointeur s'en va." width="80%">
</p>

<br>
<br>

## Un geste pour chaque chose.

<p align="center">
  <img src="../bento.jpg" alt="Clic pour copier. Clic droit pour annoter. Glisser pour partager. Lâcher, et c'est fait." width="100%">
</p>

<br>
<br>

| | |
|:--|:--|
| Clic | Copier l'image. |
| Clic droit | Un menu : copier, annoter, enregistrer, décrocher. |
| Double clic | L'ouvrir dans l'éditeur d'annotations. |
| Glisser dans une app | Envoyer une copie. Elle reste sur le fil. |
| Glisser dans un dossier | L'y garder. Elle quitte le fil. |
| Suppr | La mettre à la corbeille. |
| <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> | Capturer une région. Elle s'accroche dès que vous lâchez. |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> | Afficher ou masquer le fil. |

Clothesline ne fait jamais de capture lui-même. Il surveille les raccourcis que
Windows vous donne déjà — <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> pour une
région, <kbd>Win</kbd>+<kbd>PrintScreen</kbd> pour tout l'écran — et dès qu'une
capture atterrit, elle s'accroche au fil. Les mêmes raccourcis. La même mémoire
musculaire. Juste un endroit où les accrocher.

<br>

## Votre bureau. Enfin dégagé.

Confiez vos captures à Clothesline et elles zappent complètement le bureau.
Pas d'encombrement. Pas cinq secondes d'attente. Chaque capture s'accroche
à l'instant où vous la prenez, et seul ce que vous glissez dehors est conservé.

Vos réglages de capture précédents sont mémorisés et remis en place quand
Clothesline se ferme, et il s'efface quand une app est en plein écran.

<br>

## Les images copiées aussi.

Copiez une image depuis n'importe où —un navigateur, une fenêtre de discussion,
l'outil de capture— et elle s'accroche au fil comme les autres. Activez
**Accrocher les images copiées** dans le menu de la zone de notification et le
t-shirt se remplit pendant ce temps. Uniquement les images : ce qui est copié
avec du texte ou un fichier est laissé tranquille.

<br>

## Privé par conception.

Pas de compte. Pas de réseau. Pas de télémétrie.
Clothesline tourne entièrement sur votre PC, et vos captures n'en sortent jamais.

<br>

## Fiche technique

| | |
|:--|:--|
| **Compatibilité** | Windows 10 et 11, x64. Compatible DPI par moniteur. |
| **Taille** | ~160 Mo installés, runtime inclus |
| **Langues** | Anglais, chinois simplifié, espagnol, japonais, français |
| **Construit avec** | C# 13, WPF et WinForms sur .NET 9 |
| **Accès réseau** | Aucun |
| **Prix** | Gratuit |
| **Licence** | MIT pour le code. Le nom et l'icône ne sont pas inclus. |

<br>

## Installer

Téléchargez `Clothesline-Setup.exe` depuis la
[dernière version](https://github.com/Zhao-allen/Clothesline/releases/latest)
et lancez-le. L'installateur choisit sa propre langue, vous laisse choisir où
il va, et emporte le runtime .NET avec lui —rien d'autre à installer.

Vous préférez ne rien installer ? La version portable est un dossier :
décompressez `Clothesline-win-x64.zip` où vous voulez et lancez `Clothesline.exe`.

<br>

## Compiler depuis les sources

```bat
git clone https://github.com/Zhao-allen/Clothesline.git
cd Clothesline
build.bat
```

Nécessite le SDK .NET 9 avec la charge de travail WPF. Le script produit un
paquet autosuffisant x64 dans `build\`, si bien que le résultat tourne sur une
machine sans .NET installé.

<br>

<details>
<summary>Dans l'application</summary>
<br>

| Fichier | Rôle |
|:--|:--|
| `App.xaml.cs` | Démarrage, icône de zone, les deux surveillants |
| `UI/LineWindow.xaml.cs` | La bande transparente le long du bord supérieur |
| `UI/Layout.cs` | La corde, et où chaque photo s'accroche |
| `UI/PeggedCard.cs` | Une photo : cadre, pince à linge, balancement et chute |
| `Models/Line.cs` | Ce qui pend, et ce qu'on peut en faire |
| `Services/ScreenshotWatcher.cs` | Remarque les nouvelles captures |
| `Services/ClipboardWatcher.cs` | Remarque les images copiées |
| `Services/Inbox.cs` | Prend le dossier des captures et le rend en quittant |
| `UI/AnnotationWindow.cs` | L'éditeur d'annotations |
| `Services/FullScreen.cs` | Sait quand rester caché |
| `UI/TrayIcon.cs` | L'icône de zone et son menu |
| `Services/Diag.cs` | Le journal qui rend une panne silencieuse lisible |

Les traductions vivent dans `Resources`, un `.resx` par langue, et
`Services/I18n.cs` choisit entre elles.

</details>

<br>

---

<p align="center">
  <sub>Le code est sous licence MIT. Le nom et l'icône de Clothesline ne le sont pas, les forks ont besoin des leurs. Voir <a href="../LICENSE">LICENSE</a>.</sub>
  <br>
  <sub>Porté du <a href="https://tendedero.app">Tendedero</a> d'Alejandro Buján pour macOS. Un portage Windows indépendant, sans affiliation avec le projet original.</sub>
</p>
