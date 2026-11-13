@echo off
cd /d "%~dp0"
echo 正在后台启动 PS 工具助手……
start "" wscript //nologo "%~dp0AutoIME.PsTool.vbs" "%~dp0AutoIME.pstool.tmp" "%~dp0AutoIME.ps.pid"
echo.
echo 已启动。它会一直在后台运行，把 Photoshop 当前工具写进 AutoIME.pstool.tmp。
echo 关掉这个窗口不影响它。重启电脑后需要再双击一次（或把它放进「启动」文件夹）。
timeout /t 3 >nul