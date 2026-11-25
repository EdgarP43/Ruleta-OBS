param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Version -notmatch '^v?\d+\.\d+\.\d+$') {
    throw "Usa una version tipo 0.1.5 o v0.1.5"
}

$tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$semver = $tag.TrimStart('v')

Write-Host "Version: $tag"
$iss = Get-Content 'installer\RuletaObs.iss' -Raw
$iss = [regex]::Replace($iss, 'AppVersion=\d+\.\d+\.\d+', "AppVersion=$semver")
Set-Content -Path 'installer\RuletaObs.iss' -Value $iss -NoNewline

New-Item -ItemType Directory -Force -Path 'artifacts\publish', 'artifacts\installer' | Out-Null

Write-Host 'Publicando RuletaObs.exe...'
dotnet publish 'src\RuletaObs.App\RuletaObs.App.csproj' -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=None -o 'artifacts\publish'
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fallo' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw 'No se encontro Inno Setup 6 (ISCC.exe).' }

Write-Host 'Compilando instalador...'
& $iscc 'installer\RuletaObs.iss'
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup fallo' }

$exe = Join-Path $PSScriptRoot 'artifacts\publish\RuletaObs.exe'
$setup = Join-Path $PSScriptRoot 'artifacts\installer\RuletaObs-Setup.exe'
if (-not (Test-Path $exe) -or -not (Test-Path $setup)) {
    throw 'No se generaron los binarios esperados.'
}

Write-Host ''
Write-Host "Listo: $exe"
Write-Host "Listo: $setup"
Write-Host ''
Write-Host "Para subir el Release (necesitas gh auth login):"
Write-Host "  git tag $tag"
Write-Host "  git push origin $tag"
Write-Host "  gh release create $tag `"$exe`" `"$setup`" --title `"Ruleta OBS $tag`" --notes `"Publicado desde PC local.`""
