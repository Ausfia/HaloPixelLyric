@echo off
title HaloPixelLyric Launcher
setlocal

rem ============ 定位网易云音乐安装路径（按优先级探测） ============
set "CM_EXE="
rem 1) 从运行中的进程取路径
for /f "delims=" %%i in ('powershell -NoProfile -Command "(Get-Process cloudmusic -ErrorAction SilentlyContinue | Select-Object -First 1).Path" 2^>nul') do if not defined CM_EXE set "CM_EXE=%%i"
rem 2) 常见安装路径
if not defined CM_EXE for %%p in ("C:\Program Files\Netease\CloudMusic\cloudmusic.exe" "C:\Program Files (x86)\Netease\CloudMusic\cloudmusic.exe" "D:\Program Files\Netease\CloudMusic\cloudmusic.exe" "D:\Netease\CloudMusic\cloudmusic.exe" "E:\CloudMusic\cloudmusic.exe" "D:\CloudMusic\cloudmusic.exe") do if not defined CM_EXE if exist %%p set "CM_EXE=%%~p"
rem 3) 注册表卸载信息兜底
if not defined CM_EXE for /f "delims=" %%i in ('powershell -NoProfile -Command "(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like '*CloudMusic*' -or $_.DisplayName -like '*网易云音乐*' } | Select-Object -First 1).InstallLocation" 2^>nul') do if not defined CM_EXE set "CM_DIR=%%i"
if not defined CM_EXE if defined CM_DIR if exist "%CM_DIR%cloudmusic.exe" set "CM_EXE=%CM_DIR%cloudmusic.exe"

if not defined CM_EXE (
    echo [error] 未找到网易云音乐 cloudmusic.exe。
    echo        请用记事本打开本脚本，在 set "CM_EXE=" 一行手动填入你的网易云完整路径。
    pause
    exit /b 1
)
echo [info] 网易云路径: %CM_EXE%

rem ============ 检查 CDP 调试口 ============
curl -s -m 2 -o nul http://127.0.0.1:9222/json/version
if errorlevel 1 (
    echo [info] 调试端口未开启，正在带参数重启网易云音乐...
    taskkill /IM cloudmusic.exe /F >nul 2>&1
    timeout /t 2 /nobreak >nul
    start "" "%CM_EXE%" --remote-debugging-port=9222
    echo [info] 等待网易云启动...
    timeout /t 8 /nobreak >nul
)

start "HaloPixelLyric" /MIN "%~dp0HaloPixelLyric.exe"
echo [ok] HaloPixelLyric 已启动（控制台已最小化，歌词将随播放显示在像素屏）。
echo      首次使用请在网易云里打开一次播放页（点击底栏歌曲信息进入黑胶页）。
timeout /t 5 /nobreak >nul
