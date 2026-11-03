$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -Raw (Join-Path $projectRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
$app = Join-Path $projectRoot 'artifacts/app'
if (-not (Test-Path -LiteralPath (Join-Path $app 'RecDog.exe'))) { throw 'Run scripts/build.ps1 first.' }
$actual = (Get-Item -LiteralPath (Join-Path $app 'RecDog.dll')).VersionInfo.FileVersion
if ($actual -ne "$version.0") { throw "Build version $actual differs from $version. Rebuild first." }
$release = Join-Path $projectRoot 'artifacts/releases'
$staging = Join-Path $release ("RecDog-v$version-windows-x64")
if (Test-Path -LiteralPath $staging) { throw "Package directory already exists: $staging" }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
# Copy only program resources; never package data/, WebView2 cache or test output.
foreach ($file in Get-ChildItem -LiteralPath $app -File) {
    if ($file.Extension -in @('.exe', '.dll', '.json') -or $file.Name -in @('LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath $file.FullName -Destination $staging
    }
}
foreach ($name in @('web', 'licenses', 'runtimes', 'workers')) {
    Copy-Item -LiteralPath (Join-Path $app $name) -Destination $staging -Recurse
}
foreach ($name in @('README.md', 'README.en.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $staging
}
Get-ChildItem -LiteralPath (Join-Path $staging 'workers') -Recurse -File -Filter 'test_*.py' | Remove-Item -Force
$archive = Join-Path $release ("RecDog-v$version-windows-x64.zip")
Compress-Archive -LiteralPath $staging -DestinationPath $archive
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Host "Created $archive (requires .NET 10 Desktop Runtime x64 and WebView2)."
