@echo off
setlocal
cd /d "%~dp0"
set DST=%LOCALAPPDATA%\AutoIME
if not exist "%DST%" mkdir "%DST%"
copy /y "AutoIME.exe" "%DST%\" >nul
copy /y "AutoIME.PsTool.vbs" "%DST%\" >nul
copy /y "启动PS工具助手.cmd" "%DST%\" >nul
copy /y "启用开机自启.cmd" "%DST%\" >nul
copy /y "关闭开机自启.cmd" "%DST%\" >nul
if not exist "%DST%\AutoIME.ini" copy /y "AutoIME.ini" "%DST%\" >nul
if exist "使用说明.md" copy /y "使用说明.md" "%DST%\" >nul
taskkill /f /im AutoIME.exe >nul 2>&1
start "" "%DST%\AutoIME.exe"
echo.
echo 已安装到 %DST%
echo 已启动，请看右下角托盘图标（英 / 中 字样的小圆点）。
echo 如果 PS 里画布打不了中文，双击 %DST%\启动PS工具助手.cmd 一次。
echo 想开机自启：双击 %DST%\启用开机自启.cmd 一次。
pause