@echo off
title LinkedIn o Interpol - OpenCV Edition
echo ===================================================
echo     Iniciando LinkedIn o Interpol (C# + OpenCV)
echo ===================================================
cd /d "%~dp0"
start http://localhost:5117
dotnet run --launch-profile http
pause
