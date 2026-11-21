# Builds the front-end bundle (TypeScript + CodeMirror + vendored Vditor).
# Requires Node 20+ and is invoked automatically by `dotnet build` when dist is stale.
param(
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$web = Join-Path $PSScriptRoot "..\src\MarkTop.Web"

Push-Location $web
try {
    if ($Clean -and (Test-Path "node_modules")) {
        Remove-Item -Recurse -Force "node_modules"
    }

    if (-not (Test-Path "node_modules")) {
        npm ci --no-audit --no-fund
    }

    npm run build
}
finally {
    Pop-Location
}
