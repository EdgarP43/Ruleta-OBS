@echo off
setlocal
cd /d "%~dp0"

set "URL=http://127.0.0.1:47832/"
set "PUBLISH=%~dp0artifacts\publish\RuletaObs.exe"

powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri '%URL%' -UseBasicParsing -TimeoutSec 2; exit 0 } catch { exit 1 }" >nul 2>&1
if %ERRORLEVEL%==0 (
  start "" "%URL%"
  exit /b 0
)

if exist "%PUBLISH%" (
  start "" "%PUBLISH%"
  exit /b 0
)

echo Compilando Ruleta OBS la primera vez...
dotnet publish "src\RuletaObs.App\RuletaObs.App.csproj" -c Release -r win-x64 --self-contained true -o "artifacts\publish" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
  echo Fallo al compilar. Necesitas el SDK de .NET 10.
  pause
  exit /b 1
)
start "" "%PUBLISH%"
endlocal
