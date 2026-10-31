# Building and Running AirFlick

## 1. System Requirements

- **OS**: Windows 10 (version 1809 / build 17763 or later) or Windows 11.
- **Architecture**: x64 (primary) or ARM64.
- **Hardware**:
  - Wi-Fi adapter supporting Wi-Fi Direct (Virtual Wi-Fi Miniport).
  - Integrated webcam or USB video camera for live gesture control.

## 2. Prerequisites

1. **Visual Studio 2022** (version 17.8 or higher) with:
   - *.NET Desktop Development* workload.
   - *Windows App SDK C# Templates*.
2. **.NET 8 SDK** (v8.0.300 or later).
3. **Python 3.10+** (64-bit) available on the system PATH.

## 3. Python Environment Setup

Install the required computer vision libraries:
```powershell
pip install opencv-python mediapipe
```

Verify that the hand landmark model exists at:
`gesture/models/hand_landmarker.task`

## 4. Building the Solution

### Restore Dependencies
```powershell
dotnet restore .\AirFlick.sln
```

### Build Debug Configuration
```powershell
dotnet build .\AirFlick.csproj -c Debug -p:Platform=x64
```

### Build Release Configuration
```powershell
dotnet build .\AirFlick.csproj -c Release -p:Platform=x64
```

## 5. Publishing a Self-Contained Unpackaged Release

To build a standalone executable that runs without requiring a machine-wide .NET 8 installation:

```powershell
dotnet publish .\AirFlick.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -o artifacts\AirFlick-Portable\
```

The output folder will contain `AirFlick.exe`, all necessary .NET runtime DLLs, Windows App SDK binaries, and the `gesture\` folder.

