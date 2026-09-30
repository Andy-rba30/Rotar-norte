<#
.SYNOPSIS
  Compila el add-in para todas las versiones de Revit soportadas y deja un zip por versión en .\dist
#>
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root "src\RotarNorte\RotarNorte.csproj"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

foreach ($v in "2022", "2023", "2024", "2025", "2026", "2027") {
    Write-Host "=== Revit $v" -ForegroundColor Cyan
    dotnet build $proj -c "R$v"
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación para Revit $v" }

    $stage = Join-Path $dist "RotarNorte_Revit$v"
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $stage "RotarNorte") | Out-Null
    Copy-Item (Join-Path $root "src\RotarNorte\bin\R$v\RotarNorte.dll") (Join-Path $stage "RotarNorte")
    Copy-Item (Join-Path $root "src\RotarNorte\bin\R$v\RotarNorte.pdb") (Join-Path $stage "RotarNorte") -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root "src\RotarNorte\RotarNorte.addin") $stage
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath (Join-Path $dist "RotarNorte_Revit$v.zip") -Force
}
Write-Host "Listo: $dist" -ForegroundColor Green
