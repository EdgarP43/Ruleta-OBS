@echo off
setlocal
cd /d "%~dp0"

echo Cerrando Ruleta OBS si esta abierta...
powershell -NoProfile -Command "try { Invoke-WebRequest -Uri 'http://127.0.0.1:47832/api/shutdown' -Method POST -UseBasicParsing -TimeoutSec 3 | Out-Null } catch {}" >nul 2>&1
timeout /t 2 /nobreak >nul
taskkill /IM RuletaObs.exe /F >nul 2>&1

echo Compilando con los cambios actuales...
dotnet publish "src\RuletaObs.App\RuletaObs.App.csproj" -c Release -r win-x64 --self-contained true -o "artifacts\publish" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
  echo Fallo al compilar.
  pause
  exit /b 1
)

echo Listo. Abriendo...
start "" "%~dp0artifacts\publish\RuletaObs.exe"
endlocal
