param(
    [string]$SourceDir = "$PSScriptRoot\AirFlick-Updated",
    [string]$DestDir = "$PSScriptRoot\AirFlick-Portable-Previous",
    [string]$ZipFile = "$PSScriptRoot\AirFlick-Previous-Portable.zip",
    [string]$GestureModelsSource = "$PSScriptRoot\..\gesture\models"
)

Write-Host "Creating portable package from $SourceDir to $DestDir..."

if (-not (Test-Path $SourceDir)) {
    Write-Error "Source directory $SourceDir does not exist."
    exit 1
}

if (-not (Test-Path $DestDir)) {
    New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
}

Write-Host "Copying application binaries and dependencies..."
Copy-Item -Path "$SourceDir\*" -Destination $DestDir -Recurse -Force

# Ensure gesture/models exists and has hand_landmarker.task
$DestModelsDir = "$DestDir\gesture\models"
if (-not (Test-Path $DestModelsDir)) {
    New-Item -ItemType Directory -Force -Path $DestModelsDir | Out-Null
}

if (Test-Path "$GestureModelsSource\hand_landmarker.task") {
    Write-Host "Copying MediaPipe model hand_landmarker.task..."
    Copy-Item -Path "$GestureModelsSource\hand_landmarker.task" -Destination "$DestModelsDir\hand_landmarker.task" -Force
}

# Also ensure gesture_engine.py is present
if (Test-Path "$PSScriptRoot\..\gesture\gesture_engine.py") {
    Copy-Item -Path "$PSScriptRoot\..\gesture\gesture_engine.py" -Destination "$DestDir\gesture\gesture_engine.py" -Force
}

Write-Host "Verifying essential files in $DestDir..."
$EssentialFiles = @(
    "$DestDir\AirFlick.exe",
    "$DestDir\AirFlick.dll",
    "$DestDir\AirFlick.deps.json",
    "$DestDir\gesture\main_gesture_service.py"
)

foreach ($f in $EssentialFiles) {
    if (Test-Path $f) {
        Write-Host "  [OK] Found $f"
    } else {
        Write-Warning "  [MISSING] $f"
    }
}

if (Test-Path $ZipFile) {
    Remove-Item $ZipFile -Force
}

Write-Host "Creating portable ZIP archive: $ZipFile..."
Compress-Archive -Path "$DestDir\*" -DestinationPath $ZipFile -CompressionLevel Optimal

Write-Host "Successfully created portable package: $ZipFile"

