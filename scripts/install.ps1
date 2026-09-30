<#
.SYNOPSIS
  Compila e instala el add-in "Rotar Norte" para una versión de Revit.

.EXAMPLE
  .\scripts\install.ps1 -RevitVersion 2027
  .\scripts\install.ps1 -RevitVersion 2025 -NoBuild      # solo copiar lo ya compilado
  .\scripts\install.ps1 -RevitVersion 2024 -Uninstall
#>
param(
    [ValidateSet("2022", "2023", "2024", "2025", "2026", "2027")]
    [string]$RevitVersion = "2027",
    [switch]$NoBuild,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$root    = Split-Path $PSScriptRoot -Parent
$proj    = Join-Path $root "src\RotarNorte\RotarNorte.csproj"
$bin     = Join-Path $root "src\RotarNorte\bin\R$RevitVersion"
$addins  = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$target  = Join-Path $addins "RotarNorte"

if ($Uninstall) {
    Remove-Item (Join-Path $addins "RotarNorte.addin") -Force -ErrorAction SilentlyContinue
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Rotar Norte desinstalado de Revit $RevitVersion." -ForegroundColor Green
    exit 0
}

if (-not $NoBuild) {
    Write-Host "Compilando para Revit $RevitVersion..." -ForegroundColor Cyan
    dotnet build $proj -c "R$RevitVersion"
    if ($LASTEXITCODE -ne 0) { throw "La compilación falló." }
}

$dll = Join-Path $bin "RotarNorte.dll"
if (-not (Test-Path $dll)) { throw "No se encontró $dll. Compile primero (quite -NoBuild)." }

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $dll $target -Force
Copy-Item (Join-Path $bin "RotarNorte.pdb") $target -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "src\RotarNorte\RotarNorte.addin") $addins -Force

Write-Host ""
Write-Host "Instalado en: $target" -ForegroundColor Green
Write-Host "Manifiesto:   $(Join-Path $addins 'RotarNorte.addin')"
Write-Host "Reinicie Revit $RevitVersion y busque la pestaña 'Rotar Norte'."
