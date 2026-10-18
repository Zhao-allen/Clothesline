<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../hero.jpg">
  <img src="../hero.jpg" alt="Clothesline。スクリーンショットを物干し竿に。四枚のガラス枠入りスクリーンショットが画面上部の細い線に吊るされています。">
</picture>

<h3 align="center">macOS アプリ <a href="https://tendedero.app/?lang=ja">Tendedero</a> の Windows 移植版&nbsp;&rsaquo;</h3>

<p align="center">
  無料・オープンソース。Windows 10 と 11 対応。
  <br>
  <a href="https://github.com/Zhao-allen/Clothesline/releases/latest">ダウンロード&nbsp;&rsaquo;</a>
  &nbsp;&nbsp;
  <a href="#ソースからビルド">ソースからビルド&nbsp;&rsaquo;</a>
  <br><br>
  <a href="../README.md">English</a>&nbsp;·&nbsp;<a href="README.zh-Hans.md">简体中文</a>&nbsp;·&nbsp;<a href="README.es.md">Español</a>&nbsp;·&nbsp;日本語&nbsp;·&nbsp;<a href="README.fr.md">Français</a>
</p>

<br>

## 目に入らない。手が届く。

撮ったスクリーンショットは、すべて画面のすぐ上にある物干し竿に吊るされます。
<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> を押すと竿が下りてきて、ポインタを離すと引っ込みます。

<p align="center">
  <img src="../demo.jpg" alt="ポインタが画面上端に置かれると竿が下り、スクリーンショットが吊るされ、ポインタが離れると竿が引っ込みます。" width="80%">
</p>

<br>
<br>

## すべての操作に、ひとつのジェスチャー。

<p align="center">
  <img src="../bento.jpg" alt="クリックでコピー。右クリックで注釈。ドラッグで共有。離せばおしまい。" width="100%">
</p>

<br>
<br>

| | |
|:--|:--|
| クリック | 画像をコピー。 |
| 右クリック | メニュー：コピー、編集、保存、取り外し。 |
| ダブルクリック | 注釈エディタで開く。 |
| アプリにドラッグ | コピーを渡す。竿には残る。 |
| フォルダにドラッグ | そこに保存。竿から外れる。 |
| Delete | ごみ箱へ移動。 |
| <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> | 領域をキャプチャ。離した瞬間に吊るされる。 |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> | 竿の表示・非表示。 |

Clothesline 自身はキャプチャを取りません。Windows にもともとあるショートカット——
<kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> で領域、
<kbd>Win</kbd>+<kbd>PrintScreen</kbd> で全画面——を見守り、キャプチャが保存された
瞬間に竿へ吊るだけです。同じショートカット。同じ手の記憶。吊る場所が増えただけ。

<br>

## デスクトップ。ようやく片付く。

Clothesline にスクリーンショットを任せれば、デスクトップは完全に素通りします。
散らかりません。5 秒も待ちません。撮った瞬間に吊るされ、ドラッグして出した分だけが残ります。

以前のキャプチャ設定は覚えていて、Clothesline を終了すると元に戻ります。
アプリが全画面の間は、ちゃんと身を引きます。

<br>

## コピーした画像も、もちろん。

ブラウザでも、チャットの画面でも、スクリーンショットツールでも——どこからコピーした
画像も、ふつうに竿へ吊るされます。トレイメニューの「コピーした画像を吊る」を有効に
すると、T シャツのマークが塗りつぶされます。画像だけが対象で、テキストやファイルと
一緒にコピーされたものはそっとしておきます。

<br>

## 設計からプライベート。

アカウントなし。ネットワークなし。解析なし。
Clothesline はすべてあなたの PC の中で動き、スクリーンショットが外に出ることはありません。

<br>

## 技術仕様

| | |
|:--|:--|
| **対応環境** | Windows 10 と 11、x64。マルチモニタの DPI に対応。 |
| **サイズ** | インストール後 約 160 MB（ランタイム込み） |
| **言語** | 英語、簡体字中国語、スペイン語、日本語、フランス語 |
| **開発技術** | C# 13、WPF と WinForms（.NET 9） |
| **ネットワークアクセス** | なし |
| **価格** | 無料 |
| **ライセンス** | コードは MIT。名前とアイコンは含まれません。 |

<br>

## インストール

[最新リリース](https://github.com/Zhao-allen/Clothesline/releases/latest)から
`Clothesline-Setup.exe` をダウンロードして実行します。インストーラは言語を自分で選び、
保存先を選べ、.NET ランタイムも一緒に持ち歩きます——他に何も入れる必要はありません。

インストールしたくない場合、ポータブル版はフォルダひとつです：
`Clothesline-win-x64.zip` を好きな場所に展開して `Clothesline.exe` を起動するだけです。

<br>

## ソースからビルド

```bat
git clone https://github.com/Zhao-allen/Clothesline.git
cd Clothesline
build.bat
```

.NET 9 SDK（WPF ワークロード込み）が必要です。スクリプトは `build\` に自己完結型の
x64 パッケージを作るので、.NET が入っていないマシンでもそのまま動きます。

<br>

<details>
<summary>アプリの内側</summary>
<br>

| ファイル | 役割 |
|:--|:--|
| `App.xaml.cs` | 起動、トレイアイコン、ふたつのウォッチャ |
| `UI/LineWindow.xaml.cs` | 画面上端に沿う透明の帯 |
| `UI/Layout.cs` | 綱の形と、各写真がどこに吊るされるか |
| `UI/PeggedCard.cs` | 写真ひとつ：枠、洗濯ばさみ、揺れと落下 |
| `Models/Line.cs` | 何が吊るされていて、何ができるか |
| `Services/ScreenshotWatcher.cs` | 新しいスクリーンショットに気づく |
| `Services/ClipboardWatcher.cs` | コピーされた画像に気づく |
| `Services/Inbox.cs` | キャプチャフォルダを引き継ぎ、終了時に戻す |
| `UI/AnnotationWindow.cs` | 注釈エディタ |
| `Services/FullScreen.cs` | 隠れているべきときを知っている |
| `UI/TrayIcon.cs` | トレイアイコンとそのメニュー |
| `Services/Diag.cs` | 静かな失敗を読める形にするログ |

翻訳は `Resources` に、言語ごとに `.resx` として置かれ、
`Services/I18n.cs` が選り分けます。

</details>

<br>

---

<p align="center">
  <sub>コードは MIT ライセンスです。Clothesline の名前とアイコンは含まれないため、派生版は独自のものが必要です。<a href="../LICENSE">LICENSE</a> を参照。</sub>
  <br>
  <sub>Alejandro Buján の macOS アプリ <a href="https://tendedero.app">Tendedero</a> からの移植。独立した Windows 移植版であり、元のプロジェクトとは提携していません。</sub>
</p>
