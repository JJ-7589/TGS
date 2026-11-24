@echo off
title Centro Canino - Servidor Local
cd /d "%~dp0"
echo ============================================================
echo   Centro Canino - Salud, Estetica y Recreacion
echo   Iniciando servidor en http://localhost:5150 ...
echo   Usuario inicial: admin  |  Contrasena: 12345
echo   Cierra esta ventana para detener el servidor.
echo ============================================================
start "" cmd /c "timeout /t 2 /nobreak >nul && start http://localhost:5150/login"
"%~dp0Caninos.Api.exe"
pause
