# Builds CrossMouse.exe with the compiler that ships inside Windows itself.
#
# Two paths are supported:
#   * build.ps1               - uses csc.exe + al.exe from the running Windows
#   * build.ps1 -MSBuild      - uses an installed MSBuild (any edition)
#
# Nothing needs to be installed, no NuGet, no Visual Studio.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$MSBuild,
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    $dist = Join-Path $root "dist\$Configuration"
    $obj = Join-Path $root "obj\$Configuration"
    New-Item -ItemType Directory -Force -Path $dist, $obj | Out-Null

    $sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
    if (-not $sources) { throw 'No sources found in src/' }

    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path $frameworkDir)) { $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
    $csc = Join-Path $frameworkDir 'csc.exe'
    if (-not (Test-Path $csc)) { throw "csc.exe not found in $frameworkDir" }

    # 1. Render the icon set with the app itself (single source of truth for the artwork).
    Write-Host '==> rendering icons'
    $iconDir = Join-Path $root 'assets'
    New-Item -ItemType Directory -Force -Path $iconDir | Out-Null
    $iconArgs = @(
        '/nologo', '/target:exe', "/out:$obj\\icon-gen.exe",
        '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Core.dll'
    ) + $sources
    & $csc $iconArgs | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'icon generator failed to compile' }
    $ico = & (Join-Path $obj 'icon-gen.exe') --icon $iconDir
    if (-not (Test-Path (Join-Path $iconDir 'CrossMouse.ico'))) { throw 'icon generation failed' }
    Write-Host "    $ico"

    # 2. Icon resources are added as win32 resources at link time with al.exe/csc /win32icon.
    $versionString = if ($Version) { $Version } else { '1.0.0' }
    $props = Get-Content (Join-Path $root 'src\Properties.cs') -Raw
    $props = $props -replace 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$versionString.0`")"
    $props = $props -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$versionString.0`")"
    $props = $props -replace 'AssemblyInformationalVersion\("[^"]+"\)', "AssemblyInformationalVersion(`"$versionString`")"
    Set-Content -Path (Join-Path $obj 'Properties.cs') -Value $props -Encoding UTF8

    $compile = @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/langversion:5',
        '/codepage:65001', "/out:$dist\\CrossMouse.exe",
        "/win32icon:$iconDir\\CrossMouse.ico",
        "/win32manifest:$root\\src\\app.manifest",
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
    ) + ($sources | Where-Object { $_ -notlike '*Properties.cs' }) + (Join-Path $obj 'Properties.cs')

    if ($MSBuild) {
        Write-Host '==> building with MSBuild'
        $msbuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue
        if (-not $msbuild) { throw 'msbuild.exe not found' }
        & $msbuild.Source (Join-Path $root 'src\CrossMouse.csproj') /p:Configuration=$Configuration /v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'msbuild failed' }
    }
    else {
        Write-Host '==> compiling CrossMouse.exe'
        & $csc $compile | Write-Host
        if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
    }

    # 3. Package a zip that users can unpack anywhere.
    $zip = Join-Path $dist 'CrossMouse-win.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $staging = Join-Path $obj 'staging'
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Copy-Item (Join-Path $dist 'CrossMouse.exe') $staging
    Copy-Item (Join-Path $root 'README.md') $staging -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root 'LICENSE') $staging -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force

    $item = Get-Item (Join-Path $dist 'CrossMouse.exe')
    $hash = (Get-FileHash $item.FullName -Algorithm SHA256).Hash
    Write-Host ''
    Write-Host "built : $($item.FullName)"
    Write-Host "size  : $([math]::Round($item.Length / 1KB, 1)) KB"
    Write-Host "sha256: $hash"
    Write-Host "zip   : $zip"
}
finally {
    Pop-Location
}
