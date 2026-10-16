<#
.SYNOPSIS
    Compila el instalador de Windows de VibeScreen.

.DESCRIPTION
    El mismo script en tu máquina y en el CI, para que el instalador que se prueba en local sea el
    que sale en la release. Deja todo en artifacts/, que está en .gitignore:

      artifacts/icon/        el icono de los ficheros y las imágenes del asistente;
      artifacts/publish/     la ventana y el servicio, cada uno en su carpeta;
      artifacts/pawnio/      el instalador oficial de PawnIO, comprobado contra su hash;
      artifacts/installer/   VibeScreen-<versión>-setup.exe.

    Hace falta Inno Setup 7. Si no está: winget install --id JRSoftware.InnoSetup.7 -e

.PARAMETER Version
    La versión que llevan los ejecutables y el instalador: 0.3.0, o 0.3.1-dev.12 fuera de main.

.PARAMETER Iscc
    El ISCC.exe de Inno Setup, si no está donde se instala por defecto ni en el PATH.

.EXAMPLE
    ./packaging/windows/build.ps1 -Version 0.3.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string] $Iscc
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Sin la barra de progreso: en Windows PowerShell 5.1 hace que una descarga tarde diez veces más.
$ProgressPreference = 'SilentlyContinue'

# PawnIO va dentro del instalador porque el usuario no tiene que instalar nada aparte. Se descarga el
# oficial —se puede redistribuir sin modificar— y se comprueba contra el hash que publica GitHub: lo
# que se mete en un instalador que corre como administrador no puede ser lo que haya ese día en la URL.
$PawnIOVersion = '2.2.0'
$PawnIOSha256 = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $root 'artifacts'
$icon = Join-Path $artifacts 'icon'
$publish = Join-Path $artifacts 'publish'
$pawnio = Join-Path $artifacts 'pawnio\PawnIO_setup.exe'

function Invoke-Native {
    param([string] $FilePath, [string[]] $Arguments)

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FilePath ha terminado con el código $LASTEXITCODE" }
}

function Find-Iscc {
    if ($Iscc) { return $Iscc }
    if ($env:ISCC) { return $env:ISCC }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    throw 'No encuentro Inno Setup 7. Instálalo con «winget install --id JRSoftware.InnoSetup.7 -e» o pásame -Iscc.'
}

$iscc = Find-Iscc

Write-Host "== Icono"
Invoke-Native dotnet @('run', '--project', (Join-Path $root 'tools\VibeScreen.IconExport'), '-c', 'Release', '--', $icon)

# Cada uno en su carpeta, y se vacían antes: un fichero que sobrara de una compilación anterior
# acabaría dentro del instalador sin que nadie lo haya pedido.
Write-Host "== Publicar"
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

# Solo los satélites de los dos idiomas de la ventana. El ; va escapado porque MSBuild lo leería como
# separador de propiedades.
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', "-p:Version=$Version", '-p:SatelliteResourceLanguages=en%3Bes')

Invoke-Native dotnet (@('publish', (Join-Path $root 'src\VibeScreen.App'), '-o', (Join-Path $publish 'app'),
    "-p:VibeScreenIcon=$(Join-Path $icon 'vibescreen.ico')") + $common)

# El servicio, para Windows: es el objetivo que lleva los sensores y el gestor de servicios.
Invoke-Native dotnet (@('publish', (Join-Path $root 'src\VibeScreen.Service'), '-f', 'net10.0-windows',
    '-o', (Join-Path $publish 'service')) + $common)

Write-Host "== PawnIO $PawnIOVersion"
$valid = (Test-Path $pawnio) -and ((Get-FileHash $pawnio -Algorithm SHA256).Hash -eq $PawnIOSha256)
if (-not $valid) {
    New-Item -ItemType Directory -Force (Split-Path $pawnio) | Out-Null
    $url = "https://github.com/namazso/PawnIO.Setup/releases/download/$PawnIOVersion/PawnIO_setup.exe"
    Invoke-WebRequest $url -OutFile $pawnio -UseBasicParsing

    $hash = (Get-FileHash $pawnio -Algorithm SHA256).Hash
    if ($hash -ne $PawnIOSha256) {
        Remove-Item $pawnio
        throw "El PawnIO_setup.exe descargado no es el esperado: SHA-256 $hash"
    }
}

# Lo que se pone en los metadatos del ejecutable solo admite números.
Write-Host "== Instalador"
$numeric = ($Version -split '-')[0]
Invoke-Native $iscc @("/DAppVersion=$Version", "/DNumericVersion=$numeric", "/DArtifacts=$artifacts",
    (Join-Path $PSScriptRoot 'vibescreen.iss'))

$installer = Join-Path $artifacts "installer\VibeScreen-$Version-setup.exe"
Write-Host "Listo: $installer ($([math]::Round((Get-Item $installer).Length / 1MB, 1)) MB)"
