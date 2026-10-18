<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../hero.jpg">
  <img src="../hero.jpg" alt="Clothesline. Capturas tendidas al sol. Cuatro capturas en marcos de cristal cuelgan de una línea fina en la parte superior de la pantalla.">
</picture>

<h3 align="center">Un port para Windows de <a href="https://tendedero.app/?lang=es">Tendedero</a>&nbsp;&rsaquo;</h3>

<p align="center">
  Gratis y de código abierto. Para Windows 10 y 11.
  <br>
  <a href="https://github.com/Zhao-allen/Clothesline/releases/latest">Descargar&nbsp;&rsaquo;</a>
  &nbsp;&nbsp;
  <a href="#compilar-desde-el-código">Compilar desde el código&nbsp;&rsaquo;</a>
  <br><br>
  <a href="../README.md">English</a>&nbsp;·&nbsp;<a href="README.zh-Hans.md">简体中文</a>&nbsp;·&nbsp;Español&nbsp;·&nbsp;<a href="README.ja.md">日本語</a>&nbsp;·&nbsp;<a href="README.fr.md">Français</a>
</p>

<br>

## Fuera de la vista. Al alcance de la mano.

Cada captura que hagas cuelga de una línea justo encima de tu pantalla.
Pulsa <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> y baja. Aleja el puntero y desaparece.

<p align="center">
  <img src="../demo.jpg" alt="El puntero descansa en el borde superior, la línea baja con las capturas colgadas, y se recoge cuando el puntero se marcha." width="80%">
</p>

<br>
<br>

## Un gesto para cada cosa.

<p align="center">
  <img src="../bento.jpg" alt="Clic para copiar. Clic derecho para anotar. Arrastra para compartir. Suelta y listo." width="100%">
</p>

<br>
<br>

| | |
|:--|:--|
| Clic | Copiar la imagen. |
| Clic derecho | Un menú: copiar, editar, guardar, quitar. |
| Doble clic | Abrirla en el editor de anotaciones. |
| Arrastrar a una app | Enviar una copia. Se queda en la línea. |
| Arrastrar a una carpeta | Guardarla allí. Sale de la línea. |
| Suprimir | Moverla a la Papelera de reciclaje. |
| <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> | Capturar una región. Cuelga en cuanto sueltas. |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> | Mostrar u ocultar la línea. |

Clothesline nunca hace capturas por su cuenta. Vigila los atajos que Windows ya
te da — <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> para una región,
<kbd>Win</kbd>+<kbd>PrintScreen</kbd> para la pantalla completa — y en el momento
en que aterriza una captura, cuelga en la línea. Los mismos atajos. La misma
memoria muscular. Solo un sitio donde colgarlas.

<br>

## Tu escritorio. Por fin despejado.

Dale a Clothesline tus capturas y se saltan el Escritorio por completo.
Sin desorden. Sin esperar cinco segundos. Cada captura cuelga al instante de
hacerla, y solo se queda lo que sacas arrastrándolo.

Tu configuración anterior de capturas se recuerda y se restaura cuando
Clothesline se cierra, y se aparta cuando una app está a pantalla completa.

<br>

## También las imágenes copiadas.

Copia una imagen desde cualquier sitio —un navegador, una ventana de chat, la
herramienta de recortes— y cuelga en la línea como cualquiera otra. Activa
**Colgar imágenes copiadas** en el menú de la bandeja y la camiseta se rellena
mientras esté activo. Solo imágenes: lo que se copie junto con texto o un
archivo se deja en paz.

<br>

## Privado por diseño.

Sin cuenta. Sin red. Sin analíticas.
Clothesline funciona por completo en tu PC, y tus capturas no salen de él.

<br>

## Especificaciones

| | |
|:--|:--|
| **Compatibilidad** | Windows 10 y 11, x64. Consciente del DPI por monitor. |
| **Tamaño** | ~160 MB instalado, runtime incluido |
| **Idiomas** | Inglés, chino simplificado, español, japonés, francés |
| **Hecho con** | C# 13, WPF y WinForms sobre .NET 9 |
| **Acceso a la red** | Ninguno |
| **Precio** | Gratis |
| **Licencia** | MIT para el código. El nombre y el icono no están incluidos. |

<br>

## Instalar

Descarga `Clothesline-Setup.exe` de la
[última versión](https://github.com/Zhao-allen/Clothesline/releases/latest)
y ejecútalo. El instalador elige su propio idioma, te deja elegir dónde va, y
lleva el runtime de .NET consigo —no hay que instalar nada más.

¿Prefieres no instalar? La versión portátil es una carpeta: descomprime
`Clothesline-win-x64.zip` donde quieras y abre `Clothesline.exe`.

<br>

## Compilar desde el código

```bat
git clone https://github.com/Zhao-allen/Clothesline.git
cd Clothesline
build.bat
```

Requiere el SDK de .NET 9 con la carga de trabajo WPF. El script genera un
paquete autónomo x64 en `build\`, así que el resultado funciona en una máquina
sin .NET instalado.

<br>

<details>
<summary>Dentro de la app</summary>
<br>

| Archivo | Función |
|:--|:--|
| `App.xaml.cs` | Arranque, icono de bandeja, los dos vigilantes |
| `UI/LineWindow.xaml.cs` | La franja transparente a lo largo del borde superior |
| `UI/Layout.cs` | La cuerda, y dónde cuelga cada foto |
| `UI/PeggedCard.cs` | Una foto: marco, pinza, balanceo y caída |
| `Models/Line.cs` | Lo que cuelga, y qué puedes hacer con ello |
| `Services/ScreenshotWatcher.cs` | Se da cuenta de las capturas nuevas |
| `Services/ClipboardWatcher.cs` | Se da cuenta de las imágenes copiadas |
| `Services/Inbox.cs` | Toma la carpeta de capturas y la devuelve al salir |
| `UI/AnnotationWindow.cs` | El editor de anotaciones |
| `Services/FullScreen.cs` | Sabe cuándo quedarse escondido |
| `UI/TrayIcon.cs` | El icono de bandeja y su menú |
| `Services/Diag.cs` | El registro que hace legible un fallo silencioso |

Las traducciones viven en `Resources`, un `.resx` por idioma, y
`Services/I18n.cs` elige entre ellos.

</details>

<br>

---

<p align="center">
  <sub>El código tiene licencia MIT. El nombre y el icono de Clothesline no, así que las bifurcaciones necesitan los suyos. Ver <a href="../LICENSE">LICENSE</a>.</sub>
  <br>
  <sub>Portado del <a href="https://tendedero.app">Tendedero</a> de Alejandro Buján para macOS. Un port independiente para Windows, sin afiliación con el proyecto original.</sub>
</p>
