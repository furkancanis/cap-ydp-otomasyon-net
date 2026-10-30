@echo off
chcp 65001 >nul
cd /d "%~dp0"
title CAP / YDP Takip Otomasyonu (.NET)
where dotnet >nul 2>nul || (echo .NET 8 SDK bulunamadi: https://dotnet.microsoft.com/download/dotnet/8.0 & pause & exit /b 1)
echo Veritabani: appsettings.Local.json icindeki ConnectionStrings:Default (veya DATABASE_URL)
echo Uygulama: http://localhost:5080
start "" http://localhost:5080
dotnet run --urls http://localhost:5080
pause
