# fetch-deps.ps1 - downloads the third-party pieces the build needs.
#
# Nothing third-party is committed to this repository. Run this once after
# cloning, then run build.ps1.
#
#   1. ffmpeg (GPL build by gyan.dev)      -> payload\ffmpeg\ffmpeg.exe
#   2. LocosLab Virtual Camera (MIT)      -> vc\VirtualCamera.msi
#                                          -> vc-inspect\ (managed assembly + licence)
#
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$payload = Join-Path $root 'payload'
$vc = Join-Path $root 'vc'
$vcInspect = Join-Path $root 'vc-inspect'

New-Item -ItemType Directory -Path $payload -Force | Out-Null
New-Item -ItemType Directory -Path $vc -Force | Out-Null
New-Item -ItemType Directory -Path $vcInspect -Force | Out-Null

# ---------------------------------------------------------------- ffmpeg --
$ffmpegExe = Join-Path $payload 'ffmpeg\ffmpeg.exe'
if (Test-Path $ffmpegExe) {
    Write-Host "ffmpeg already present: $ffmpegExe" -ForegroundColor DarkGray
} else {
    $url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
    $zip = Join-Path $payload 'ffmpeg-essentials.zip'
    Write-Host 'downloading ffmpeg (essentials, ~110 MB)...' -ForegroundColor Cyan
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing

    $tmp = Join-Path $payload 'ffmpeg-tmp'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive $zip -DestinationPath $tmp -Force

    $bin = Get-ChildItem $tmp -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $bin) { throw 'ffmpeg.exe not found inside the archive' }

    New-Item -ItemType Directory -Path (Split-Path $ffmpegExe -Parent) -Force | Out-Null
    Copy-Item $bin.FullName $ffmpegExe -Force

    $licence = Join-Path $bin.DirectoryName '..\LICENSE'
    if (Test-Path $licence) {
        Copy-Item $licence (Join-Path (Split-Path $ffmpegExe -Parent) 'SOURCE-LICENSE.txt') -Force
    }
    Write-Host "ffmpeg -> $ffmpegExe" -ForegroundColor Green
}

# --------------------------------------------------------- LocosLab media --
# Provides the Media Foundation custom media source (MFCreateVirtualCamera)
# that turns our frame pipe into a real camera device.
$msi = Join-Path $vc 'VirtualCamera.msi'
if (Test-Path $msi) {
    Write-Host "VirtualCamera.msi already present" -ForegroundColor DarkGray
} else {
    $url = 'https://github.com/locosmac/virtual-camera/releases/download/1.0.0/VirtualCamera-x64-1.0.0.zip'
    $zip = Join-Path $root 'VirtualCamera-x64-1.0.0.zip'
    Write-Host 'downloading LocosLab Virtual Camera...' -ForegroundColor Cyan
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing

    $tmp = Join-Path $payload 'locoslab-tmp'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive $zip -DestinationPath $tmp -Force

    Copy-Item (Join-Path $tmp 'VirtualCamera.msi') $msi -Force
    Write-Host "VirtualCamera.msi -> $msi" -ForegroundColor Green
}

# the build needs the managed assembly to compile against; pull it straight
# out of the MSI with an administrative install (no admin rights needed)
if (-not (Get-ChildItem $vcInspect -Recurse -Filter 'LocosLab.VirtualCamera.dll' -ErrorAction SilentlyContinue)) {
    Write-Host 'extracting the managed assembly from the MSI...' -ForegroundColor Cyan
    if (Test-Path $vcInspect) { Remove-Item $vcInspect -Recurse -Force }
    New-Item -ItemType Directory -Path $vcInspect -Force | Out-Null
    $p = Start-Process msiexec -ArgumentList "/a", "`"$msi`"", '/qb', "TARGETDIR=$vcInspect" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "msiexec /a failed with $($p.ExitCode)" }
} else {
    Write-Host 'managed assembly already extracted' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host 'dependencies ready - now run: .\build.ps1' -ForegroundColor Green
