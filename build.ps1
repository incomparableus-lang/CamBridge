# build.ps1 - builds CamBridge.exe, Uninstall.exe and the self-extracting CamBridge-Setup.exe
#
# Prerequisites: run .\fetch-deps.ps1 once (downloads ffmpeg and the LocosLab
# media source). Everything else comes from Windows itself - no Visual Studio,
# no MSBuild, no NuGet.
#
#   .\fetch-deps.ps1
#   .\build.ps1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root  = Split-Path -Parent $MyInvocation.MyCommand.Path
$src   = Join-Path $root 'src'
$stage = Join-Path $root 'stage'
$dist  = Join-Path $root 'dist'
$csc   = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$fx    = "$env:windir\Microsoft.NET\Framework64\v4.0.30319"
$refs  = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"

function Ref([string]$name) {
    $a = Join-Path $fx $name
    if (Test-Path $a) { return $a }
    $b = Join-Path $refs $name
    if (Test-Path $b) { return $b }
    throw "reference assembly not found: $name"
}

# The LocosLab managed assembly is installed by the MSI at run time, so on a clean
# build machine it is not in the GAC - fall back to the copy fetch-deps extracted.
$locosCandidates = @()
$gacRoot = Join-Path $env:windir 'Microsoft.NET\assembly\GAC_MSIL\LocosLab.VirtualCamera'
if (Test-Path $gacRoot) {
    $locosCandidates += Get-ChildItem $gacRoot -Recurse -Filter 'LocosLab.VirtualCamera.dll' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName }
}
$locosCandidates += Get-ChildItem (Join-Path $root 'vc-inspect') -Recurse -Filter 'LocosLab.VirtualCamera.dll' -ErrorAction SilentlyContinue |
    ForEach-Object { $_.FullName }
$locos = $locosCandidates | Select-Object -First 1
if (-not $locos) { throw 'LocosLab.VirtualCamera.dll not found - run .\fetch-deps.ps1 first' }
Write-Host ("using " + $locos) -ForegroundColor DarkGray

# ---------------------------------------------------------------- version --
$versionFile = Join-Path $root 'VERSION'
$version = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { '0.0.0-dev' }
if ($version -notmatch '^\d+\.\d+\.\d+') { throw "VERSION must look like 1.2.3, got '$version'" }
Write-Host ("version " + $version) -ForegroundColor DarkGray

$generated = Join-Path $stage 'GeneratedVersion.cs'
$gv = @"
[assembly: System.Reflection.AssemblyTitle("CamBridge")]
[assembly: System.Reflection.AssemblyProduct("CamBridge")]
[assembly: System.Reflection.AssemblyCompany("CamBridge")]
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@

function Compile($out, $sources, $references) {
    # /codepage:65001 - the sources carry Cyrillic, CJK, Arabic and Devanagari text;
    # without this csc would fall back to the ANSI code page and mangle everything
    $args = @('/nologo', '/codepage:65001', '/target:winexe', '/platform:x64', '/optimize+', "/out:$out")
    foreach ($r in $references) { $args += "/r:$r" }
    $args += $sources
    Write-Host ("  csc -> " + (Split-Path $out -Leaf)) -ForegroundColor DarkGray
    & $csc $args
    if ($LASTEXITCODE -ne 0) { throw "compile failed: $out" }
}

Write-Host '== clean ==' -ForegroundColor Cyan
foreach ($d in @($stage, $dist)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Path $stage | Out-Null
New-Item -ItemType Directory -Path $dist | Out-Null
Set-Content -Path $generated -Value $gv -Encoding UTF8

$core    = Join-Path $src 'BridgeCore.cs'
$strings = Join-Path $src 'Strings.cs'
$winForms = Ref 'System.Windows.Forms.dll'
$drawing  = Ref 'System.Drawing.dll'
$compression = Ref 'System.IO.Compression.dll'

Write-Host '== compile ==' -ForegroundColor Cyan
Compile (Join-Path $stage 'CamBridge.exe') @($core, $strings, $generated, (Join-Path $src 'CamBridge.cs')) `
    @($winForms, $drawing, $locos)
Compile (Join-Path $stage 'Uninstall.exe') @($core, $strings, $generated, (Join-Path $src 'Uninstaller.cs')) `
    @($winForms, $drawing, $locos)
Compile (Join-Path $stage 'SetupStub.exe') @($core, $strings, $generated, (Join-Path $src 'Setup.cs')) `
    @($winForms, $drawing, $compression, $locos)

Write-Host '== stage payload ==' -ForegroundColor Cyan
$ffmpegExe = Join-Path $root 'payload\ffmpeg\ffmpeg.exe'
if (-not (Test-Path $ffmpegExe)) { throw "missing ffmpeg: $ffmpegExe`nrun .\fetch-deps.ps1 first" }
New-Item -ItemType Directory -Path (Join-Path $stage 'ffmpeg') | Out-Null
Copy-Item $ffmpegExe (Join-Path $stage 'ffmpeg\ffmpeg.exe') -Force

$msi = Join-Path $root 'vc\VirtualCamera.msi'
if (-not (Test-Path $msi)) { throw "missing msi: $msi`nrun .\fetch-deps.ps1 first" }
Copy-Item $msi (Join-Path $stage 'VirtualCamera.msi') -Force

# ship the licences of everything we redistribute
$licences = @(
    @{ src = Join-Path $root 'payload\ffmpeg\SOURCE-LICENSE.txt'; dst = 'LICENSE-ffmpeg.txt' },
    @{ src = Join-Path $root 'payload\ffmpeg-tmp'; dst = $null; glob = 'LICENSE'; sub = 'LICENSE-ffmpeg.txt' },
    @{ src = Join-Path $root 'vc-inspect\License.rtf';     dst = 'LICENSE-locoslab.rtf' }
)
foreach ($l in $licences) {
    if ($l.glob) {
        $found = Get-ChildItem $l.src -Recurse -Filter $l.glob -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { Copy-Item $found.FullName (Join-Path $stage $l.sub) -Force }
    } elseif (Test-Path $l.src) {
        Copy-Item $l.src (Join-Path $stage $l.dst) -Force
    }
}

Write-Host '== pack payload ==' -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $dist 'payload.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
"payload.zip: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB) | Write-Host

Write-Host '== assemble installer ==' -ForegroundColor Cyan
$setup = Join-Path $dist 'CamBridge-Setup.exe'
Copy-Item (Join-Path $stage 'SetupStub.exe') $setup -Force
$payload = [System.IO.File]::ReadAllBytes($zip)
$fs = [System.IO.File]::Open($setup, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Write)
try {
    $null = $fs.Seek(0, [System.IO.SeekOrigin]::End)
    $fs.Write($payload, 0, $payload.Length)
} finally { $fs.Dispose() }

"stub       : {0:N0} bytes" -f (Get-Item (Join-Path $stage 'SetupStub.exe')).Length | Write-Host
"installer  : {0:N1} MB" -f ((Get-Item $setup).Length / 1MB) | Write-Host
Write-Host ''
Write-Host ("built: " + $setup) -ForegroundColor Green
