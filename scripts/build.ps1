$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (Test-Path -LiteralPath 'D:\DevTools\dotnet\dotnet.exe') {
        $env:DOTNET_ROOT = 'D:\DevTools\dotnet'
        $env:NUGET_PACKAGES = 'D:\DevTools\nuget-packages'
        $env:DOTNET_CLI_HOME = 'D:\DevTools\dotnet-cli-home'
        $dotnetCommand = 'D:\DevTools\dotnet\dotnet.exe'
    } else { $dotnetCommand = 'dotnet' }
    $env:CI = 'true'
    & pnpm --dir frontend run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    & $dotnetCommand build src/RecDog.Desktop/RecDog.Desktop.csproj -c Release -o artifacts/app-recording-workflow
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed. Close the new RecDog window before rebuilding.' }
    Write-Host 'Built artifacts/app-recording-workflow/RecDog.exe. Launch with Start-RecDog.cmd.'
} finally { Pop-Location }
