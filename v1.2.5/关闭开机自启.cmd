@echo off
chcp 936 >nul
set LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\AutoIME.vbs
if exist "%LNK%" (
  del /f /q "%LNK%"
  echo 已关闭开机自启。
) else (
  echo 本来就没有开启。
)
echo.
pause