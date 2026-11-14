# AmdPanel build script (PowerShell) - one-click Release|x64 build
# ===========================================================
# Requires:
#   - Visual Studio 2022 17.8+ OR Visual Studio 2022 Build Tools
#     (component "Desktop development with C++" + ".NET 8 SDK" + "WPF .NET")
#   - .NET 8 SDK
# Auto-detects MSBuild.exe via vswhere.exe (no manual VS path configuration)
# ===========================================================

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  AmdPanel v1.0.0 - One-click Build" -ForegroundColor Cyan
Write-Host "  Config: $Configuration | Platform: $Platform" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: locate vswhere.exe ---
$vswhereList = @(
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
    "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe"
)
$vswhere = $vswhereList | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $vswhere) {
    Write-Host "ERROR: vswhere.exe not found." -ForegroundColor Red
    Write-Host "Visual Studio 2022 (or Build Tools) is not installed." -ForegroundColor Yellow
    Write-Host "Download: https://visualstudio.microsoft.com/downloads/" -ForegroundColor Yellow
    Write-Host "Required components:" -ForegroundColor Yellow
    Write-Host "  - Desktop development with C++" -ForegroundColor Yellow
    Write-Host "  - .NET 8 SDK" -ForegroundColor Yellow
    Write-Host "  - WPF (.NET)" -ForegroundColor Yellow
    exit 1
}
Write-Host "[1/6] Found vswhere: $vswhere" -ForegroundColor Green

# --- Step 2: locate MSBuild.exe ---
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Workload.NativeDesktop Microsoft.VisualStudio.Workload.ManagedDesktop `
    -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) {
    Write-Host "ERROR: MSBuild.exe not found via vswhere." -ForegroundColor Red
    Write-Host "Required VS components are not installed:" -ForegroundColor Yellow
    Write-Host "  - Microsoft.VisualStudio.Workload.NativeDesktop (Desktop C++)" -ForegroundColor Yellow
    Write-Host "  - Microsoft.VisualStudio.Workload.ManagedDesktop (.NET desktop)" -ForegroundColor Yellow
    Write-Host "Open Visual Studio Installer → Modify → check both workloads." -ForegroundColor Yellow
    exit 2
}
Write-Host "[2/6] Found MSBuild: $msbuild" -ForegroundColor Green

# --- Step 3: locate .NET 8 SDK ---
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "ERROR: dotnet CLI not found on PATH." -ForegroundColor Red
    Write-Host ".NET 8 SDK is required. Download: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 3
}
$dotnetVer = & dotnet --version
Write-Host "[3/6] Found .NET SDK: $dotnetVer" -ForegroundColor Green

# --- Step 4: restore NuGet packages ---
Write-Host "[4/6] Restoring NuGet packages..." -ForegroundColor Cyan
& dotnet restore AmdPanel.sln --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { Write-Host "dotnet restore failed." -ForegroundColor Red; exit 4 }

# --- Step 5: build NativeBridge (C++) first ---
Write-Host "[5/6] Building AmdPanel.NativeBridge (C++, $Configuration|$Platform)..." -ForegroundColor Cyan
$bridgeProj = Join-Path $repoRoot "src\AmdPanel.NativeBridge\AmdPanel.NativeBridge.vcxproj"
& $msbuild $bridgeProj /t:Build /p:Configuration=$Configuration /p:Platform=$Platform /m /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "NativeBridge build failed." -ForegroundColor Red; exit 5 }

# Copy NativeBridge.dll to Core.Hardware output so subsequent C# builds pick it up
$bridgeOutDir = Join-Path $repoRoot "src\AmdPanel.NativeBridge\$Platform\$Configuration"
$bridgeDll = Join-Path $bridgeOutDir "AmdPanel.NativeBridge.dll"
$bridgePdb = Join-Path $bridgeOutDir "AmdPanel.NativeBridge.pdb"
$buildNativeDir = Join-Path $repoRoot "build\NativeBridge"
New-Item -ItemType Directory -Force -Path $buildNativeDir | Out-Null
Copy-Item $bridgeDll -Destination $buildNativeDir -Force
if (Test-Path $bridgePdb) { Copy-Item $bridgePdb -Destination $buildNativeDir -Force }
Write-Host "  Copied: $bridgeDll -> $buildNativeDir" -ForegroundColor DarkGray

# --- Step 6: build AmdPanel.App (.NET 8 WPF) ---
Write-Host "[6/6] Building AmdPanel.App (.NET 8 WPF, $Configuration|$Platform)..." -ForegroundColor Cyan
& dotnet build (Join-Path $repoRoot "src\AmdPanel.App\AmdPanel.App.csproj") `
    -c $Configuration -p:Platform=$Platform --no-restore --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Write-Host "AmdPanel.App build failed." -ForegroundColor Red; exit 6 }

# --- Stage release directory ---
$releaseName = "AmdPanel-v1.0.0-win-x64"
$distDir = Join-Path $repoRoot "dist"
$stageDir = Join-Path $distDir $releaseName
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

# Locate AmdPanel.exe output
$appOutDir = Join-Path $repoRoot "src\AmdPanel.App\bin\$Configuration\net8.0-windows10.0.19041.0"
if (-not (Test-Path $appOutDir)) {
    # Fallback: net8.0-windows
    $appOutDir = Join-Path $repoRoot "src\AmdPanel.App\bin\$Configuration\net8.0-windows"
}
if (-not (Test-Path (Join-Path $appOutDir "AmdPanel.exe"))) {
    Write-Host "ERROR: AmdPanel.exe not found in $appOutDir" -ForegroundColor Red
    exit 7
}
Write-Host "Staging release to: $stageDir" -ForegroundColor Cyan
Copy-Item (Join-Path $appOutDir "*") -Destination $stageDir -Recurse -Force

# Ensure AmdPanel.NativeBridge.dll is in the stage
$nativeBridgeInStage = Join-Path $stageDir "AmdPanel.NativeBridge.dll"
if (-not (Test-Path $nativeBridgeInStage)) {
    Copy-Item $bridgeDll -Destination $stageDir -Force
    Write-Host "  Copied AmdPanel.NativeBridge.dll from build\NativeBridge" -ForegroundColor DarkGray
}

# Copy LICENSE and README.md
Copy-Item (Join-Path $repoRoot "LICENSE") -Destination $stageDir -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $repoRoot "README.md") -Destination $stageDir -Force -ErrorAction SilentlyContinue

# --- Zip release ---
if (-not $NoZip) {
    $zipPath = Join-Path $distDir "$releaseName.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "Packaging: $zipPath" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Green
    Write-Host "  BUILD SUCCESS" -ForegroundColor Green
    Write-Host "  Release: $zipPath" -ForegroundColor Green
    Write-Host "  Stage:   $stageDir" -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Green
    Write-Host "  BUILD SUCCESS (no zip)" -ForegroundColor Green
    Write-Host "  Stage: $stageDir" -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Green
}
