# Kuroko アプリアイコン(.ico)生成スクリプト
# assets/kuroko-icon.svg のシルエットを GDI+ で描き、16〜256px のマルチサイズ .ico を出力する。
# 依存追加なしで再生成できるよう PowerShell + System.Drawing で実装。
param(
    [string]$OutPath = (Join-Path $PSScriptRoot 'kuroko.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = 16,24,32,48,64,128,256
$pngs = New-Object System.Collections.Generic.List[byte[]]

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $sc = $s / 100.0

    # 生成り色の角丸地
    $d = 44.0 * $sc   # 角丸直径 (半径22)
    $x0 = 2.0 * $sc; $y0 = 2.0 * $sc; $w = 96.0 * $sc; $h = 96.0 * $sc
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc([single]$x0, [single]$y0, [single]$d, [single]$d, 180, 90)
    $bg.AddArc([single]($x0 + $w - $d), [single]$y0, [single]$d, [single]$d, 270, 90)
    $bg.AddArc([single]($x0 + $w - $d), [single]($y0 + $h - $d), [single]$d, [single]$d, 0, 90)
    $bg.AddArc([single]$x0, [single]($y0 + $h - $d), [single]$d, [single]$d, 90, 90)
    $bg.CloseFigure()
    $kinari = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(244, 240, 233))
    $g.FillPath($kinari, $bg)

    # 墨黒のフードシルエット
    $hood = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hood.AddLine([single](22*$sc), [single](86*$sc), [single](22*$sc), [single](48*$sc))
    $hood.AddBezier([single](22*$sc),[single](48*$sc), [single](22*$sc),[single](27*$sc), [single](34*$sc),[single](14*$sc), [single](50*$sc),[single](14*$sc))
    $hood.AddBezier([single](50*$sc),[single](14*$sc), [single](66*$sc),[single](14*$sc), [single](78*$sc),[single](27*$sc), [single](78*$sc),[single](48*$sc))
    $hood.AddLine([single](78*$sc), [single](48*$sc), [single](78*$sc), [single](86*$sc))
    $hood.CloseFigure()
    $sumi = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(26, 21, 18))
    $g.FillPath($sumi, $hood)

    # 柿色の目もとスリット
    $kaki = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(214, 90, 46))
    $g.FillRectangle($kaki, [single](34*$sc), [single](47*$sc), [single](32*$sc), [single](5*$sc))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray())
    $bmp.Dispose()
}

# ICO コンテナを書き出す(各画像はPNG格納。Vista以降が対応)
$fs = [System.IO.File]::Open($OutPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)              # reserved
$bw.Write([UInt16]1)              # type = icon
$bw.Write([UInt16]$sizes.Count)   # image count
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$dim)         # width
    $bw.Write([Byte]$dim)         # height
    $bw.Write([Byte]0)            # palette
    $bw.Write([Byte]0)            # reserved
    $bw.Write([UInt16]1)          # planes
    $bw.Write([UInt16]32)         # bpp
    $bw.Write([UInt32]$len)       # size
    $bw.Write([UInt32]$offset)    # offset
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
$fs.Close()
Write-Output "Wrote $OutPath ($($sizes.Count) sizes)"
