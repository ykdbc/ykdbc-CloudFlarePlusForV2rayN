param(
    [Parameter(Mandatory = $true)]
    [string]$V2rayNDirectory,

    [string]$PackageDirectory = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $builtPackageDirectory = Join-Path $root "dist\v2rayN-auto-switch-plugin"
    $packageEntry = Join-Path $root "v2rayN.AutoSwitchCompanion.exe"
    if (Test-Path -LiteralPath $builtPackageDirectory) {
        $PackageDirectory = $builtPackageDirectory
    }
    elseif (Test-Path -LiteralPath $packageEntry) {
        $PackageDirectory = $root
    }
    else {
        $PackageDirectory = $builtPackageDirectory
    }
}

$resolvedHost = Resolve-Path -LiteralPath $V2rayNDirectory
$hostExe = Join-Path $resolvedHost.Path "v2rayN.exe"
if (-not (Test-Path -LiteralPath $hostExe)) {
    throw "v2rayN.exe was not found in: $($resolvedHost.Path)"
}

$resolvedPackage = Resolve-Path -LiteralPath $PackageDirectory
$entry = Join-Path $resolvedPackage.Path "v2rayN.AutoSwitchCompanion.exe"
if (-not (Test-Path -LiteralPath $entry)) {
    throw "Plugin package entry was not found: $entry. Run build-plugin.ps1 first."
}

$protectedHostFiles = @(
    "v2rayN.exe",
    "v2rayN.dll",
    "v2rayN.Desktop.exe",
    "v2rayN.Desktop.dll",
    "AmazTool.exe",
    "AmazTool.dll",
    "av_libglesv2.dll",
    "D3DCompiler_47_cor3.dll",
    "e_sqlite3.dll",
    "libHarfBuzzSharp.dll",
    "libSkiaSharp.dll",
    "PenImc_cor3.dll",
    "PresentationNative_cor3.dll",
    "vcruntime140_cor3.dll",
    "wpfgfx_cor3.dll"
)

Get-ChildItem -LiteralPath $resolvedPackage.Path -File | ForEach-Object {
    if ($_.Name -in @("v2rayN.exe", "v2rayN.dll", "v2rayN.Desktop.exe", "v2rayN.Desktop.dll", "AmazTool.exe", "AmazTool.dll")) {
        throw "Refusing to install host-owned file from plugin package: $($_.Name)"
    }

    $destination = Join-Path $resolvedHost.Path $_.Name
    if ($_.Name -in $protectedHostFiles -and (Test-Path -LiteralPath $destination)) {
        Write-Host "Keeping existing host file: $($_.Name)"
        return
    }

    Copy-Item -LiteralPath $_.FullName -Destination $resolvedHost.Path -Force
}

Write-Host "Installed auto-switch companion to: $($resolvedHost.Path)"
