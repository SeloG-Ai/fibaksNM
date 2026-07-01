@echo off
chcp 1254 > nul
title C# Projesi Derleme Araci
echo ==================================================
echo         FIBAKS KARGO OTOMASYONU DERLEME ARACI
echo ==================================================
echo.
echo C# kaynak kodlari (Program.cs) exe dosyasina derleniyor...
echo.

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF_DIR=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF

if not exist %CSC% (
    echo [HATA] .NET Framework C# Derleyicisi csc.exe bulunamadi.
    echo Luften .NET Framework 4.0 veya uzeri bir surumun kurulu oldugundan emin olun.
    pause
    exit /b
)

%CSC% /r:System.Windows.Forms.dll /r:"%WPF_DIR%\UIAutomationClient.dll" /r:"%WPF_DIR%\UIAutomationTypes.dll" /out:"%~dp0FibaksFetcher.exe" "%~dp0Program.cs"

if %errorlevel% == 0 (
    echo.
    echo [BASARILI] FibaksFetcher.exe basariyla olusturuldu.
    echo.
) else (
    echo.
    echo [HATA] Derleme sirasinda bir hata olustu.
    echo.
)
pause
