@echo off
rem LocalVault kurulumu: çift tıklayın. Ek seçenekler için install.ps1 -? komutuna bakın.
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -DesktopShortcut %*
if errorlevel 1 (
  echo.
  echo Kurulum basarisiz oldu. Yukaridaki hata mesajina bakin.
)
echo.
pause
