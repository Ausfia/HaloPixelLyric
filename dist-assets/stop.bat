@echo off
taskkill /IM HaloPixelLyric.exe /F >nul 2>&1
"%~dp0HaloPixelLyric.exe" --clock
echo [ok] 已停止，像素屏恢复时钟模式。
pause
