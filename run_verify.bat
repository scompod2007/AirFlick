@echo off
setlocal
echo Running tests...
dotnet test .\AirFlick.Tests\AirFlick.Tests.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Tests failed with exit code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
echo Building AirFlick Release x64...
dotnet build .\AirFlick.csproj -c Release -p:Platform=x64
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Build failed with exit code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
echo [SUCCESS] Both tests and build completed successfully!

