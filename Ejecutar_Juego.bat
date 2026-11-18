@echo off
title LinkedIn o Interpol - Web Edition (C# + OpenCV)
echo ===================================================
echo     Iniciando LinkedIn o Interpol (Web C# + OpenCV)
echo ===================================================
cd /d "%~dp0"
dotnet run --project src\Backend.Web\LinkedInOrInterpolClone.csproj --launch-profile http
pause
