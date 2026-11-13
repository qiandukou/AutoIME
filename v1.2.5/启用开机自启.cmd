@echo off
chcp 936 >nul
set SRC=%~dp0AutoIME.exe
set LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\AutoIME.vbs
if not exist "%SRC%" (echo 找不到 AutoIME.exe，请把这个脚本放在 AutoIME 同目录。 & pause & exit /b 1)
> "%LNK%" echo CreateObject("WScript.Shell").Run """%SRC%""", 0, False
if exist "%LNK%" (
  echo.
  echo 已启用开机自启。
  echo 启动器：%LNK%
) else (
  echo.
  echo 写入失败，可能被安全软件拦了。
  echo 请手动：Win+R 输入 shell:startup，把 AutoIME 的快捷方式拖进去。
)
echo.
pause