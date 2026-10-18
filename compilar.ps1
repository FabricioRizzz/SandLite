# Gera saida\SandLite-Instalador.exe (um arquivo só, com o app e o motor dentro).
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force saida | Out-Null

if (-not (Test-Path caixa.ico)) { & powershell -NoProfile -ExecutionPolicy Bypass -File ferramentas\gerar_icone.ps1 }

& $csc /nologo /codepage:65001 /target:winexe /optimize+ /win32icon:caixa.ico /out:saida\SandLite.exe SandLite.cs
if ($LASTEXITCODE) { throw "falha ao compilar SandLite.exe" }

# cada arquivo vira um recurso comprimido "f:<caminho dentro da pasta de instalação>"
Remove-Item saida\gz -Recurse -Force -ErrorAction SilentlyContinue
function Gz($src, $name) {
    $dst = Join-Path "saida\gz" ($name -replace "[/\\]", "_")
    New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
    $in = [IO.File]::OpenRead($src); $out = [IO.File]::Create((Join-Path $PSScriptRoot $dst))
    $z = New-Object IO.Compression.GZipStream $out, ([IO.Compression.CompressionMode]::Compress)
    $in.CopyTo($z); $z.Close(); $in.Close()
    "/resource:`"$dst`",f:$name"
}
$res = @(Gz (Resolve-Path saida\SandLite.exe) "SandLite.exe")
Get-ChildItem motor -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path motor).Path.Length + 1).Replace('\', '/')
    $res += Gz $_.FullName "Motor/$rel"
}
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /win32icon:caixa.ico /win32manifest:instalador.manifest /out:saida\SandLite-Instalador.exe @res Instalador.cs
if ($LASTEXITCODE) { throw "falha ao compilar o instalador" }
"OK: saida\SandLite-Instalador.exe ({0:N1} MB)" -f ((Get-Item saida\SandLite-Instalador.exe).Length / 1MB)
