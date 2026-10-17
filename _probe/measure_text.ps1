# Measures Clothesline's painted strings with the real fonts, to quantify the
# fixed-width (296px) tray header strip. No GUI: GDI+ text metrics only.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 8, 8
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

function Measure-Str([string]$s, [float]$size, [string]$style) {
    $st = [System.Drawing.FontStyle]::$style
    $f = New-Object System.Drawing.Font('Segoe UI Variable Display', $size, $st)
    $w = $g.MeasureString($s, $f).Width
    $f.Dispose()
    return [math]::Round($w, 1)
}

# ---- header strip geometry (from TrayIcon.cs) ----
# Width = 296 ; subtitle drawn at x = 59 ; badge.X = 296 - bw - 14
$W = 296
$subX = 59
$prefix = 'v0.1.9 · '

$locales = [ordered]@{
    'en' = @{ watching = 'Watching the clipboard'; off = 'Clipboard watch is off'; badge = '8 on the line' }
    'es' = @{ watching = 'Vigilando el portapapeles'; off = 'Vigilancia desactivada'; badge = '8 en la línea' }
    'zh' = @{ watching = '正在监视剪贴板'; off = '剪贴板监视已关'; badge = '线上 8 张' }
    'ja' = @{ watching = 'クリップボードを監視中'; off = '監視はオフです'; badge = 'ライン上 8 枚' }
    'fr' = @{ watching = 'Surveillance du presse-papiers'; off = 'Surveillance désactivée'; badge = '8 sur la corde' }
}

Write-Output "== TRAY HEADER STRIP (fixed 296px) : subtitle vs right-aligned count badge =="
Write-Output ("{0,-4} {1,-8} {2,7} {3,7} {4,8} {5,8} {6,8}" -f 'loc','state','subW','subEnd','badgeW','badgeX','verdict')
foreach ($loc in $locales.Keys) {
    foreach ($state in @('watching','off')) {
        $text = $prefix + $locales[$loc][$state]
        $subW = Measure-Str $text 8.0 'Regular'
        $subEnd = $subX + $subW
        $bw = [math]::Ceiling((Measure-Str $locales[$loc]['badge'] 8.5 'Bold')) + 18
        $badgeX = $W - $bw - 14
        $verdict = if ($subEnd -gt $badgeX) { 'OVERLAP ' + [math]::Round($subEnd - $badgeX,1) + 'px' } else { 'ok' }
        Write-Output ("{0,-4} {1,-8} {2,7} {3,7} {4,8} {5,8} {6,8}" -f $loc, $state, $subW, $subEnd, $bw, $badgeX, $verdict)
    }
}

Write-Output ""
Write-Output "== EmptyHint (thumbnail strip, drawn at x=14, no width bound, strip=296) =="
$empty = [ordered]@{
    'en' = 'Press Win+Shift+S to take a screenshot'
    'es' = 'Pulsa Win+Shift+S para hacer una captura'
    'zh' = '按 Win+Shift+S 截图'
    'ja' = 'Win+Shift+S でスクリーンショット'
    'fr' = 'Appuyez sur Win+Shift+S pour capturer'
}
foreach ($loc in $empty.Keys) {
    $w = Measure-Str $empty[$loc] 8.0 'Regular'
    $end = 14 + $w
    $verdict = if ($end -gt 296) { 'OVERFLOW ' + [math]::Round($end - 296,1) + 'px' } else { 'ok' }
    Write-Output ("{0,-4} {1,7} end={2,7} {3}" -f $loc, $w, [math]::Round($end,1), $verdict)
}

Write-Output ""
Write-Output "== Longest MENU row labels (10pt; WinForms menu auto-sizes, baseline zh=357px) =="
$rows = @(
    @('en','When the pointer rests at the top edge'),
    @('en','Take down everything to the right'),
    @('fr','Quand le pointeur se pose au bord supérieur'),
    @('fr','Descendre tout ce qui est à droite'),
    @('fr','Descendre tout ce qui est à gauche'),
    @('fr','Valeur système par défaut'),
    @('es','Cuando el puntero reposa en el borde superior'),
    @('es','Quitar todo lo de la izquierda'),
    @('ja','ポインタが上端に留まった時'),
    @('zh','当指针停在屏幕顶部')
)
foreach ($r in $rows) {
    $w = Measure-Str $r[1] 10.0 'Regular'
    # row width = icon margin(33) + icon(16+gap) + text + right pad; approximate
    $approx = [math]::Round(33 + 24 + $w + 40, 0)
    Write-Output ("{0,-4} {1,6}px  approx-row={2,5}px  {3}" -f $r[0], $w, $approx, $r[1])
}
