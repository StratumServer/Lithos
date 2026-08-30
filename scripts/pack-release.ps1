[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Rids,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$Version,
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionDocument = [xml](Get-Content (Join-Path $repoRoot 'version.props') -Raw)
$sourceVersion = [string]$versionDocument.Project.PropertyGroup.LithosVersion
$manifest = Get-Content (Join-Path $repoRoot 'forks.json') -Raw | ConvertFrom-Json
$gameVersion = [string]$manifest.vintageStoryVersion

if (-not $Version) {
    $Version = $sourceVersion
}
if ($Version.StartsWith('v')) {
    $Version = $Version.Substring(1)
}
if ($Version -ne $sourceVersion) {
    throw "Release version '$Version' does not match version.props '$sourceVersion'."
}
if (-not $gameVersion) {
    throw 'forks.json does not define vintageStoryVersion.'
}

$supportedRids = @('linux-x64', 'win-x64')
foreach ($rid in $Rids) {
    if ($rid -notin $supportedRids) {
        throw "Unsupported release RID '$rid'. Supported RIDs: $($supportedRids -join ', ')."
    }
}

$outPath = if ([System.IO.Path]::IsPathRooted($OutDir)) {
    [System.IO.Path]::GetFullPath($OutDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutDir))
}
$packageWorkspace = Join-Path $repoRoot '.lithos/package'
$launcherProject = Join-Path $repoRoot 'launcher/Lithos.Server/Lithos.Server.csproj'

Push-Location $repoRoot
try {
    if (-not $NoBuild) {
        & dotnet build Lithos.slnx -c $Configuration -p:Version=$Version -p:InformationalVersion=$Version -nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }

    New-Item -ItemType Directory -Force -Path $outPath | Out-Null
    if (Test-Path $packageWorkspace) {
        Remove-Item -LiteralPath $packageWorkspace -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $packageWorkspace | Out-Null

    foreach ($rid in $Rids) {
        $publish = Join-Path $packageWorkspace "publish-$rid"
        $stage = Join-Path $packageWorkspace "stage-$rid"
        New-Item -ItemType Directory -Force -Path $stage | Out-Null

        Write-Host "Publishing Lithos $Version for $rid"
        & dotnet publish $launcherProject `
            -c $Configuration `
            -r $rid `
            --self-contained false `
            -p:PublishSingleFile=true `
            -p:EmbedPatchedFiles=true `
            -p:DebugSymbols=false `
            -p:DebugType=None `
            -p:Version=$Version `
            -p:InformationalVersion=$Version `
            -o $publish `
            -nologo
        if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed for $rid." }

        $executable = if ($rid.StartsWith('win-')) { 'LithosServer.exe' } else { 'LithosServer' }
        Copy-Item -LiteralPath (Join-Path $publish $executable) -Destination $stage
        $runtimeConfig = Join-Path $publish 'LithosServer.runtimeconfig.json'
        if (Test-Path $runtimeConfig) {
            Copy-Item -LiteralPath $runtimeConfig -Destination $stage
        }
        Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $stage
        Copy-Item -LiteralPath (Join-Path $repoRoot 'NOTICE') -Destination $stage
        Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $stage

        $archive = Join-Path $outPath "lithos-$Version-vs$gameVersion-$rid.zip"
        if (Test-Path $archive) {
            Remove-Item -LiteralPath $archive -Force
        }
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
        Write-Host "Wrote $archive"
    }
}
finally {
    Pop-Location
    if (Test-Path $packageWorkspace) {
        Remove-Item -LiteralPath $packageWorkspace -Recurse -Force
    }
}
