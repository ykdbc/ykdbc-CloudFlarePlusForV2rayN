param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$V2rayNSourceDirectory = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\v2rayN.AutoSwitchCompanion\v2rayN.AutoSwitchCompanion.csproj"
$output = Join-Path $root "dist\v2rayN-auto-switch-plugin"
$rawOutput = Join-Path $root "dist\publish-raw"
$defaultSource = Join-Path $root "..\v2rayN\v2rayN"

if ([string]::IsNullOrWhiteSpace($V2rayNSourceDirectory)) {
    $V2rayNSourceDirectory = $defaultSource
}

$resolvedSource = Resolve-Path -LiteralPath $V2rayNSourceDirectory
$serviceLibProject = Join-Path $resolvedSource.Path "ServiceLib\ServiceLib.csproj"
if (-not (Test-Path -LiteralPath $serviceLibProject)) {
    throw "ServiceLib.csproj was not found under V2rayNSourceDirectory: $($resolvedSource.Path)"
}

function Remove-PluginBuildDirectory {
    param([string]$Path)

    $distRoot = Join-Path $root "dist"
    $resolvedDistRoot = [System.IO.Path]::GetFullPath($distRoot)
    $resolvedTarget = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolvedTarget.StartsWith($resolvedDistRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove directory outside plugin dist folder: $resolvedTarget"
    }

    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

Remove-PluginBuildDirectory $output
Remove-PluginBuildDirectory $rawOutput

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $rawOutput `
    /p:V2rayNSourceRoot="$($resolvedSource.Path)"

New-Item -ItemType Directory -Force -Path $output | Out-Null

$blockedHostFiles = @(
    "v2rayN.exe",
    "v2rayN.dll",
    "v2rayN.Desktop.exe",
    "v2rayN.Desktop.dll",
    "AmazTool.exe",
    "AmazTool.dll"
)

Get-ChildItem -LiteralPath $rawOutput -File | ForEach-Object {
    if ($_.Name -notin $blockedHostFiles) {
        Copy-Item -LiteralPath $_.FullName -Destination $output -Force
    }
}

$entryExe = Join-Path $output "v2rayN.AutoSwitchCompanion.exe"
if (Test-Path -LiteralPath $entryExe) {
    Copy-Item -LiteralPath $entryExe -Destination (Join-Path $output "CloudFlarePlusForV2rayN.exe") -Force
}

Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "autoswitch-companion.example.json") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "plugin-manifest.json") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "install-plugin.ps1") -Destination $output -Force

Remove-PluginBuildDirectory $rawOutput

Write-Host "Published plugin package to: $output"
