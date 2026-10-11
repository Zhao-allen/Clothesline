<#
.SYNOPSIS
    Clothesline Windows build / publish script.

.DESCRIPTION
    Builds and publishes Clothesline as a self-contained single-file executable
    for Windows x64. No .NET runtime install required on the target machine.

.PARAMETER Configuration
    Build configuration: Release (default) or Debug.

.PARAMETER Runtime
    Target runtime identifier. Default: win-x64.

.PARAMETER OutputDir
    Output directory for the published files. Default: publish.

.PARAMETER Clean
    Remove bin/, obj/ and the output directory before building.

.PARAMETER Zip
    Compress the published output into a zip archive next to OutputDir.

.EXAMPLE
    .\build.ps1
        Build and publish a self-contained Clothesline.exe into .\publish.

.EXAMPLE
    .\build.ps1 -Clean -Zip
        Clean, publish, and produce Clothesline-win-x64.zip.

.EXAMPLE
    .\build.ps1 -Configuration Debug
        Publish a debug build (larger, for troubleshooting only).
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [string]$OutputDir = "publish",

    [switch]$Clean,

    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$ProjectFile = "Clothesline.csproj"
$AppName = "Clothesline"

function Write-Step { param([string]$Message) Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Message) Write-Host "    $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "    ! $Message" -ForegroundColor Yellow }

# ---------------------------------------------------------------------------
# 1. Pre-flight
# ---------------------------------------------------------------------------
Write-Step "Checking prerequisites"

if (-not (Test-Path $ProjectFile)) {
    throw "Project file '$ProjectFile' not found. Run this script from the project root."
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw ".NET SDK was not found on PATH. Install the .NET 9 SDK (with WPF workload) from https://dotnet.microsoft.com/download"
}

$sdk = dotnet --version
Write-Ok ".NET SDK $sdk"

# ---------------------------------------------------------------------------
# 2. Clean
# ---------------------------------------------------------------------------
if ($Clean) {
    Write-Step "Cleaning previous build artifacts"
    foreach ($dir in @("bin", "obj", $OutputDir)) {
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force
            Write-Ok "Removed $dir"
        }
    }
}

# ---------------------------------------------------------------------------
# 3. Restore
# ---------------------------------------------------------------------------
Write-Step "Restoring dependencies"
dotnet restore $ProjectFile --runtime $Runtime
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
Write-Ok "Restore complete"

# ---------------------------------------------------------------------------
# 4. Build
# ---------------------------------------------------------------------------
Write-Step "Building ($Configuration | $Runtime)"
dotnet build $ProjectFile -c $Configuration -r $Runtime --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }
Write-Ok "Build complete"

# ---------------------------------------------------------------------------
# 5. Publish (self-contained single-file)
# ---------------------------------------------------------------------------
Write-Step "Publishing self-contained single-file executable"

$publishArgs = @(
    "publish", $ProjectFile,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "--no-build",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $OutputDir
)

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exePath = Join-Path $OutputDir "$AppName.exe"
if (-not (Test-Path $exePath)) {
    throw "Publish finished but expected executable not found at '$exePath'."
}

$exeSize = (Get-Item $exePath).Length
Write-Ok ("Published {0} ({1:N1} MB)" -f $exePath, ($exeSize / 1MB))

# ---------------------------------------------------------------------------
# 6. Zip (optional)
# ---------------------------------------------------------------------------
if ($Zip) {
    Write-Step "Creating zip archive"
    $zipName = "{0}-{1}.zip" -f $AppName, $Runtime
    $zipPath = Join-Path (Get-Location) $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Compress-Archive -Path (Join-Path $OutputDir "*") -DestinationPath $zipPath
    $zipSize = (Get-Item $zipPath).Length
    Write-Ok ("Archive {0} ({1:N1} MB)" -f $zipPath, ($zipSize / 1MB))
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Step "Done"
Write-Ok "Executable: $exePath"
if ($Zip) { Write-Ok "Archive:    $zipPath" }
Write-Host ""
