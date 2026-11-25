@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Desinstalar Ruleta OBS

echo.
echo === Desinstalar Ruleta OBS ===
echo.

echo Cerrando Ruleta OBS si esta abierta...
powershell -NoProfile -Command "try { Invoke-WebRequest -Uri 'http://127.0.0.1:47832/api/shutdown' -Method POST -UseBasicParsing -TimeoutSec 2 | Out-Null } catch {}" >nul 2>&1
timeout /t 2 /nobreak >nul
taskkill /IM RuletaObs.exe /F >nul 2>&1

set "INSTALLED=%LOCALAPPDATA%\Programs\RuletaObs\unins000.exe"
if exist "%INSTALLED%" (
  echo Ejecutando desinstalador oficial...
  "%INSTALLED%" /VERYSILENT /NORESTART
  timeout /t 2 /nobreak >nul
) else (
  echo No hay instalacion oficial; se limpia portable/accesos.
)

echo Quitando accesos del Escritorio...
del /q "%USERPROFILE%\Desktop\Ruleta OBS.lnk" >nul 2>&1
del /q "%USERPROFILE%\OneDrive\Desktop\Ruleta OBS.lnk" >nul 2>&1
del /q "%USERPROFILE%\Desktop\RuletaObs-Setup.exe" >nul 2>&1
del /q "%USERPROFILE%\OneDrive\Desktop\RuletaObs-Setup.exe" >nul 2>&1
del /q "%USERPROFILE%\Desktop\Actualizar Ruleta OBS.lnk" >nul 2>&1
del /q "%USERPROFILE%\OneDrive\Desktop\Actualizar Ruleta OBS.lnk" >nul 2>&1

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v RuletaObs /f >nul 2>&1

echo.
choice /C SN /M "Tambien borrar datos locales (cola, config, token) en %%AppData%%\RuletaObs"
if errorlevel 2 goto keep_data
if errorlevel 1 goto wipe_data

:wipe_data
echo Borrando %APPDATA%\RuletaObs ...
rmdir /s /q "%APPDATA%\RuletaObs" >nul 2>&1
echo Datos locales eliminados.
goto done

:keep_data
echo Se conservan los datos en %APPDATA%\RuletaObs
goto done

:done
echo.
echo Listo.
pause
endlocal
