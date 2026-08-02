# One-click play helper for a cloned Vibe Snake checkout.
# Usage: ./play.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $Root

$venvPython = Join-Path $Root ".venv\Scripts\python.exe"
if (Test-Path -LiteralPath $venvPython) {
    & $venvPython -m vibesnake @args
    exit $LASTEXITCODE
}

if (Get-Command vibesnake -ErrorAction SilentlyContinue) {
    & vibesnake @args
    exit $LASTEXITCODE
}

Write-Host "No virtual environment found."
Write-Host "Run ./scripts/install_player.ps1 first, or:"
Write-Host "  py -3.14 -m venv .venv"
Write-Host "  .\.venv\Scripts\Activate.ps1"
Write-Host "  python -m pip install --require-hashes --only-binary=:all: -r requirements-runtime.lock"
Write-Host "  python -m pip install --no-deps --no-build-isolation -e ."
Write-Host "  ./play.ps1"
exit 1
