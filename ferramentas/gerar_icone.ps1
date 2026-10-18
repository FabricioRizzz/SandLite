# Gera caixa.ico (caixa de papelão) em vários tamanhos, para o SandLite.
param([string]$Saida = "$PSScriptRoot\..\caixa.ico")
Add-Type -AssemblyName System.Drawing

function P($x, $y) { New-Object Drawing.PointF $x, $y }

function Desenhar([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform($s / 256.0, $s / 256.0)
    $borda = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(92, 56, 26)), 7
    $borda.LineJoin = 'Round'

    $frente  = @((P 28 104), (P 128 150), (P 128 244), (P 28 198))
    $lado    = @((P 128 150), (P 228 104), (P 228 198), (P 128 244))
    $topo    = @((P 28 104), (P 128 58), (P 228 104), (P 128 150))
    # abas abertas por cima
    $abaEsq  = @((P 28 104), (P 128 58), (P 96 30), (P 4 70))
    $abaDir  = @((P 128 58), (P 228 104), (P 252 72), (P 160 30))

    $cor = { param($r, $gg, $b) New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb($r, $gg, $b)) }
    $g.FillPolygon((& $cor 196 140 84), $abaEsq);  $g.DrawPolygon($borda, $abaEsq)
    $g.FillPolygon((& $cor 176 120 66), $abaDir);  $g.DrawPolygon($borda, $abaDir)
    $g.FillPolygon((& $cor 120 76 38), $topo);     $g.DrawPolygon($borda, $topo)
    $g.FillPolygon((& $cor 214 160 98), $frente);  $g.DrawPolygon($borda, $frente)
    $g.FillPolygon((& $cor 180 124 70), $lado);    $g.DrawPolygon($borda, $lado)

    # fita adesiva descendo pela frente
    $fita = @((P 64 121), (P 92 134), (P 92 226), (P 64 213))
    $g.FillPolygon((& $cor 238 214 170), $fita)
    if ($s -ge 32) {
        # setinha "este lado para cima" no lado direito
        $seta = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(92, 56, 26)), 6
        $g.DrawLine($seta, 178, 205, 178, 150)
        $g.DrawLine($seta, 166, 166, 178, 150)
        $g.DrawLine($seta, 190, 160, 178, 150)
    }
    $g.Dispose()
    return $bmp
}

$tamanhos = 16, 24, 32, 48, 64, 128, 256
# 256 vai em PNG (padrão do Windows); os menores em bitmap 32 bits, que todo programa lê
function Dib($bmp) {
    $s = $bmp.Width
    $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
    $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2)); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]0); $bw.Write([UInt32]($s * $s * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A) } }
    $maskRow = [Math]::Ceiling($s / 32) * 4
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    , $ms.ToArray()
}
$pngs = foreach ($t in $tamanhos) {
    $b = Desenhar $t
    if ($t -ge 256) { $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); , $ms.ToArray() }
    else { Dib $b }
}

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$tamanhos.Count)
$offset = 6 + 16 * $tamanhos.Count
for ($i = 0; $i -lt $tamanhos.Count; $i++) {
    $t = $tamanhos[$i]; $dim = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[IO.File]::WriteAllBytes((Resolve-Path (Split-Path $Saida)).Path + "\" + (Split-Path $Saida -Leaf), $out.ToArray())
"Ícone gerado: $Saida"
