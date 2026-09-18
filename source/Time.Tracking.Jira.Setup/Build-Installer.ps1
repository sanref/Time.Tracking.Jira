# Build-Installer.ps1
# Compila Time.Tracking.Jira en Release y genera el instalador/actualizador con Inno Setup.
#
# El .exe resultante sirve tanto para instalar desde cero como para actualizar una
# instalacion previa sin tocar la configuracion del usuario (ver Time.Tracking.Jira.iss).

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Tiene que coincidir con el TargetFramework del csproj y con SourceDir en Time.Tracking.Jira.iss
$targetFramework    = "net10.0-windows"
$runtimeIdentifier  = "win-x64"

$root       = Split-Path $PSScriptRoot -Parent
$appDir     = Join-Path $root "Time.Tracking.Jira"
$issFile    = Join-Path $PSScriptRoot "Time.Tracking.Jira.iss"
$publishDir = Join-Path $appDir "bin\$Configuration\$targetFramework\$runtimeIdentifier\publish"
$exePath    = Join-Path $publishDir "Time.Tracking.Jira.exe"

# ----- 1. Localizar herramientas -----
function Find-InnoSetup {
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        $candidate = Join-Path $base "Inno Setup 6\ISCC.exe"
        if (Test-Path $candidate) { return $candidate }
    }
    $key = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" -ErrorAction SilentlyContinue
    if ($key -and $key.InstallLocation) {
        $candidate = Join-Path $key.InstallLocation "ISCC.exe"
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    Write-Error "dotnet no encontrado. Instala el SDK de .NET 10 desde https://dotnet.microsoft.com/download"
}

$innoSetup = Find-InnoSetup
if (-not $innoSetup) {
    Write-Error ("Inno Setup 6 no encontrado. Instalalo desde https://jrsoftware.org/isdl.php " +
                 "o con: winget install JRSoftware.InnoSetup")
}

Write-Host "dotnet:     $dotnet"
Write-Host "Inno Setup: $innoSetup"
Write-Host ""

# ----- 2. Publicar -----
# Framework-dependent: el runtime no viaja en el paquete, el instalador verifica que este.
Write-Host "=== Publicando Time.Tracking.Jira ($Configuration, $runtimeIdentifier) ===" -ForegroundColor Cyan
& $dotnet publish "$appDir\Time.Tracking.Jira.csproj" `
    --configuration $Configuration `
    --runtime $runtimeIdentifier `
    --self-contained false `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish fallo con codigo $LASTEXITCODE" }

if (!(Test-Path $exePath)) { Write-Error "No se genero $exePath" }

# El instalador toma su version del ejecutable: sin una version real no se puede
# distinguir una actualizacion de una reinstalacion.
$version = (Get-Item $exePath).VersionInfo.FileVersion
Write-Host ""
Write-Host "Version compilada: $version"
if ([string]::IsNullOrWhiteSpace($version) -or $version -like "0.0.0*") {
    Write-Error ("La version del ejecutable es '$version'. Revisa que GitVersion pueda ejecutarse " +
                 "(Build\UpdateAssemblyVersion.ps1) antes de publicar el instalador.")
}

# ----- 3. Generar el instalador -----
Write-Host ""
Write-Host "=== Generando instalador con Inno Setup ===" -ForegroundColor Cyan
& $innoSetup $issFile
if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup fallo con codigo $LASTEXITCODE" }

$installer = Get-ChildItem (Join-Path $PSScriptRoot "Output") -Filter "*.exe" |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host ""
Write-Host "=== Instalador generado ===" -ForegroundColor Green
Write-Host $installer.FullName -ForegroundColor Yellow
Write-Host ""
Write-Host "Actualizacion desatendida:" -ForegroundColor DarkGray
Write-Host "  `"$($installer.Name)`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -ForegroundColor DarkGray
