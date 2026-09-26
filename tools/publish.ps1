# Builds a self-contained Windows release of the application (no .NET installation needed on the school PC).
# Usage (PowerShell, from the repository root):   ./tools/publish.ps1
# Output: ./publish/CentreSoutien/  — copy this folder to the PC and run CentreSoutien.exe.

param(
    [string]$Output = "publish/CentreSoutien",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
dotnet test tests/CentreSoutien.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

dotnet publish src/CentreSoutien.Desktop/CentreSoutien.Desktop.csproj `
    -c Release -r $Runtime --self-contained true `
    -p:PublishReadyToRun=true `
    -o $Output
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

Write-Host ""
Write-Host "Application publiée dans $Output"
Write-Host "Lancez CentreSoutien.exe. Première connexion : admin / admin (changement de mot de passe obligatoire)."
