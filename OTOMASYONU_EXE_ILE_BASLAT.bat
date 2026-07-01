@echo off
chcp 1254 > nul
title Fibaks Kargo Otomasyonu (C# Surumu)
:: Yonetici yetkisi kontrolu ve otomatik yukseltme (UAC)
net session >nul 2>&1
if %errorLevel% == 0 (
    goto :admin
) else (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:admin
cls
if not exist "%~dp0FibaksFetcher.exe" (
    echo [HATA] FibaksFetcher.exe bulunamadi.
    echo Lutfen once compile.bat dosyasini calistirarak projeyi derleyin.
    pause
    exit /b
)

echo =======================================================
echo     FIBAKS KARGO OTOMASYONU AKTIF (C# SURUMU)
echo =======================================================
echo.
echo  * Lutfen bu siyah pencereyi KAPATMAYIN.
echo  * Surat Kargo uygulamasinin acik oldugundan emin olun.
echo  * Otomasyon arka planda siparisleri otomatik sorguluyor...
echo.
echo  [Durdurmak icin klavyeden ESC tusuna basili tutun]
echo =======================================================
echo.
"%~dp0FibaksFetcher.exe"
pause
