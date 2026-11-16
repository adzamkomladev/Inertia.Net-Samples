param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [Parameter(Mandatory = $true)][string]$Version
)

# Builds a Release artifact the way the Dockerfile does: client + SSR bundles beside the published DLL.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dir = @{
    inkwell = 'samples/Inkwell/src/Inkwell'
    carparker = 'samples/CarParker/src/CarParker'
    techlandier = 'samples/TechLandier/src/TechLandier'
}[$App]
$name = $App.Substring(0, 1).ToUpper() + $App.Substring(1)
$project = Join-Path $root $dir
$out = Join-Path $PSScriptRoot "artifacts/$name-$Version"

Push-Location $project
try {
    bun install --frozen-lockfile
    if ($LASTEXITCODE) { throw 'bun install failed' }
    bun --bun vite build
    if ($LASTEXITCODE) { throw 'client build failed' }
    bun --bun vite build --ssr
    if ($LASTEXITCODE) { throw 'ssr build failed' }
} finally { Pop-Location }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $project "$name.csproj") -c Release -o $out /p:UseAppHost=false
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Copy-Item -Recurse -Force (Join-Path $project 'wwwroot/build') (Join-Path $out 'wwwroot/build')
Copy-Item -Recurse -Force (Join-Path $project 'ssr') (Join-Path $out 'ssr')
Write-Host "Published $out"
