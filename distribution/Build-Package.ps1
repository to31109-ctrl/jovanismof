param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [Parameter(Mandatory=$true)][string]$BepInExZip,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    # owner/repo that players should receive updates from. Leave empty to ship a package
    # that never updates itself.
    [string]$UpdateRepository = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory to avoid stale build files.' }
$expected = 'F9D128E162269579B67E91A4923764F3D54FA8C8162A238CE6657D704084334C'
if ((Get-FileHash -LiteralPath $BepInExZip -Algorithm SHA256).Hash -ne $expected) { throw 'Expected the pinned BepInEx IL2CPP x64 build 752 archive.' }
Push-Location $repo
try {
    # Never package a fault that has already reached players once.
    # The check throws on failure, and ErrorActionPreference Stop carries that out of here.
    # Do not test $LASTEXITCODE: calling a .ps1 does not set it, so a stale value from an
    # earlier native command would fail a perfectly good build.
    & (Join-Path $PSScriptRoot 'Check-KnownFaults.ps1')
    dotnet build src/plugin/MegabonkTogether.Plugin.csproj -c Release -p:CI=true -p:DefineConstants=TRACE "-p:MegabonkPath=$GamePath" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    $stage = Join-Path $OutputDirectory 'BonkLink-Playtest'
    $payload = Join-Path $stage 'payload'
    New-Item -ItemType Directory -Force -Path $payload | Out-Null
    Expand-Archive -LiteralPath $BepInExZip -DestinationPath $payload
    $plugin = Join-Path $payload 'BepInEx\plugins\MegabonkTogether'
    New-Item -ItemType Directory -Force -Path $plugin | Out-Null
    Get-ChildItem -LiteralPath 'src/plugin/bin/Release/net6.0' -File | Where-Object Extension -in @('.dll','.json','.toml') | Copy-Item -Destination $plugin
    Copy-Item -LiteralPath 'src/plugin/bin/Release/net6.0/runtimes' -Destination $plugin -Recurse
    foreach ($name in @('Install.cmd','Install.ps1','README.txt','Play.vbs','Splash.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage }
    if ($UpdateRepository) {
        if ($UpdateRepository -notmatch '^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$') { throw "UpdateRepository must look like owner/repo, got '$UpdateRepository'." }
        Set-Content -LiteralPath (Join-Path $stage 'update-repository.txt') -Value $UpdateRepository -Encoding UTF8
    }
    Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $stage 'LICENSE-GPL-2.0.txt')
    Copy-Item -LiteralPath 'BONKLINK.md' -Destination $stage
    $sources = Join-Path $OutputDirectory 'source'
    New-Item -ItemType Directory -Force -Path $sources | Out-Null
    $files = git ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate corresponding source.' }
    foreach ($file in $files) {
        if ($file -match '(^|/)(bin|obj|stripped-libs|\.git)/' -or $file -match '\.(dll|pdb|exe|zip)$') { continue }
        $source = Join-Path $repo $file
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $destination = Join-Path $sources $file
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
    Compress-Archive -Path (Join-Path $sources '*') -DestinationPath (Join-Path $stage 'source.zip')
    $notices = Join-Path $stage 'ThirdParty'
    New-Item -ItemType Directory -Force -Path $notices | Out-Null
    $assets = Get-Content -Raw 'src/plugin/obj/project.assets.json' | ConvertFrom-Json
    $packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    foreach ($package in $assets.libraries.PSObject.Properties) {
        if ($package.Value.type -ne 'package') { continue }
        $packagePath = Join-Path $packageRoot $package.Value.path
        $noticeTarget = Join-Path $notices ($package.Name.Replace('/','-'))
        $licenseFiles = Get-ChildItem -LiteralPath $packagePath -Recurse -File | Where-Object { $_.Name -match '^(license|notice|copying)' -or $_.Extension -eq '.nuspec' }
        if ($licenseFiles) {
            New-Item -ItemType Directory -Force -Path $noticeTarget | Out-Null
            foreach ($licenseFile in $licenseFiles) {
                $relative = $licenseFile.FullName.Substring($packagePath.Length).TrimStart('\')
                $destination = Join-Path $noticeTarget $relative
                New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
                Copy-Item -LiteralPath $licenseFile.FullName -Destination $destination
            }
        }
    }
    @'
BepInEx IL2CPP x64 6.0.0-be.752, commit dd0655fe9d7473b18b0af759448adaf272878fcf
Unmodified loader distribution: https://builds.bepinex.dev/projects/bepinex_be/752
Source: https://github.com/BepInEx/BepInEx/tree/dd0655fe9d7473b18b0af759448adaf272878fcf
Package metadata and available license/notice files for NuGet dependencies are included here.
Megabonk Together and BonkLink edition source and GPL-2.0 license are in source.zip.
No Megabonk game binaries or BonkWithFriends binaries/source are included.
'@ | Set-Content -LiteralPath (Join-Path $notices 'README.txt')
    # Install the staged package for real and prove the launcher finds the game, so the
    # "Could not find Megabonk.exe" failure cannot be shipped again.
    & (Join-Path $PSScriptRoot 'Verify-Package.ps1') -StageDirectory $stage -GamePath $GamePath

    Write-Host "Staged at $stage. Add the verified VALIDATION.txt before creating the final ZIP."
} finally { Pop-Location }
